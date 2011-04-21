﻿#region Copyright (c) 2009-2010 LOKAD SAS. All rights reserved.

// Copyright (c) 2009-2010 LOKAD SAS. All rights reserved.
// You must not remove this notice, or any other, from this software.
// This document is the property of LOKAD SAS and must not be disclosed.

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lokad.Cqrs.Core.Transport.Contracts_v2;
using Lokad.Cqrs.Evil;
using ProtoBuf;

namespace Lokad.Cqrs.Core.Transport
{
	public interface IMessageEnvelopeSerializer
	{
		byte[] SaveReferenceMessage(MessageReference reference);
		byte[] SaveDataMessage(MessageEnvelope builder, IMessageSerializer serializer);
		bool TryReadAsReference(byte[] buffer, out MessageReference reference);
		MessageEnvelope ReadDataMessage(byte[] buffer, IMessageSerializer serializer);
	}

	public class MessageUtil
	{
		const string RefernceSignature = "[cqrs-ref-r1]";
		static readonly byte[] Reference = Encoding.Unicode.GetBytes(RefernceSignature);

		public static byte[] SaveReferenceMessage(MessageReference reference)
		{
			// important to use \r\n
			var builder = new StringBuilder();
			builder
				.Append("[cqrs-ref-r1]\r\n")
				.Append(reference.EnvelopeId).Append("\r\n")
				.Append(reference.StorageContainer).Append("\r\n")
				.Append(reference.StorageReference);

			return Encoding.Unicode.GetBytes(builder.ToString());
		}


		public static byte[] SaveDataMessage(MessageEnvelope builder, IMessageSerializer serializer)
		{
			return SaveData(builder, serializer);
		}

		public static bool TryReadAsReference(byte[] buffer, out MessageReference reference)
		{
			if (BytesStart(buffer, Reference))
			{
				string text = Encoding.Unicode.GetString(buffer);
				string[] args = text.Split(new[] {"\r\n"}, StringSplitOptions.RemoveEmptyEntries);
				reference = new MessageReference(args[1], args[2], args[3]);
				return true;
			}
			reference = null;
			return false;
		}

		static bool BytesStart(byte[] buffer, byte[] signature)
		{
			if (buffer.Length < signature.Length)
				return false;

			for (int i = 0; i < signature.Length; i++)
			{
				if (buffer[i] != signature[i])
					return false;
			}

			return true;
		}

		static IDictionary<string, object> AttributesFromContract(IEnumerable<Schema2EnvelopeAttributeContract> attributes)
		{
			var dict = new Dictionary<string, object>();

			foreach (Schema2EnvelopeAttributeContract attribute in attributes)
			{
				switch (attribute.Type)
				{
					case Schema2EnvelopeAttributeTypeContract.CreatedUtc:
						dict[MessageAttributes.Envelope.CreatedUtc] = DateTimeOffset.Parse(attribute.StringValue);
						break;
					case Schema2EnvelopeAttributeTypeContract.Sender:
						dict[MessageAttributes.Envelope.Sender] = attribute.CustomName;
						break;
					case Schema2EnvelopeAttributeTypeContract.CustomString:
						dict[attribute.CustomName] = attribute.StringValue;
						break;
					case Schema2EnvelopeAttributeTypeContract.CustomNumber:
						dict[attribute.CustomName] = attribute.NumberValue;
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}
			return dict;
		}

		static IDictionary<string, object> AttributesFromContract(IEnumerable<Schema2ItemAttributeContract> attributes)
		{
			var dict = new Dictionary<string, object>();

			foreach (Schema2ItemAttributeContract attribute in attributes)
			{
				switch (attribute.Type)
				{
					case Schema2ItemAttributeTypeContract.CustomString:
						dict[attribute.CustomName] = attribute.StringValue;
						break;
					case Schema2ItemAttributeTypeContract.CustomNumber:
						dict[attribute.CustomName] = attribute.NumberValue;
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}
			return dict;
		}

		static Schema2ItemAttributeContract[] ItemAttributesToContract(ICollection<KeyValuePair<string, object>> attributes)
		{
			var contracts = new Schema2ItemAttributeContract[attributes.Count];
			int pos = 0;

			foreach (var attrib in attributes)
			{
				switch (attrib.Key)
				{
					default:
						contracts[pos] = new Schema2ItemAttributeContract();
						throw new NotImplementedException("serializing item attributes is not supported now");
				}

				pos += 1;
			}

			return contracts;
		}

		static Schema2EnvelopeAttributeContract[] EnvelopeAttributesToContract(
			ICollection<KeyValuePair<string, object>> attributes)
		{
			var contracts = new Schema2EnvelopeAttributeContract[attributes.Count];
			int pos = 0;

			foreach (var attrib in attributes)
			{
				switch (attrib.Key)
				{
					case MessageAttributes.Envelope.CreatedUtc:
						contracts[pos] = new Schema2EnvelopeAttributeContract
							{
								Type = Schema2EnvelopeAttributeTypeContract.CreatedUtc,
								StringValue = ((DateTimeOffset) attrib.Value).ToString("o")
							};
						break;
					case MessageAttributes.Envelope.Sender:
						contracts[pos] = new Schema2EnvelopeAttributeContract
							{
								Type = Schema2EnvelopeAttributeTypeContract.Sender,
								StringValue = (string) attrib.Value
							};
						break;
					default:
						if (attrib.Value is string)
						{
							contracts[pos] = new Schema2EnvelopeAttributeContract
								{
									Type = Schema2EnvelopeAttributeTypeContract.CustomString,
									CustomName = attrib.Key,
									StringValue = (string) attrib.Value
								};
						}
						else if ((attrib.Value is long) || (attrib.Value is int) || (attrib.Value is short))
						{
							contracts[pos] = new Schema2EnvelopeAttributeContract
								{
									Type = Schema2EnvelopeAttributeTypeContract.CustomNumber,
									CustomName = attrib.Key,
									NumberValue = Convert.ToInt64(attrib.Value)
								};
						}
						else
						{
							throw new NotSupportedException("serialization of generic attributes is not supported yet");
						}
						break;
				}
				pos += 1;
			}

			return contracts;
		}

		public static MessageEnvelope ReadDataMessage(byte[] buffer, IMessageSerializer serializer)
		{
			MessageHeader header;
			using (var stream1 = new MemoryStream(buffer, 0, MessageHeader.FixedSize))
			{
				header = Serializer.Deserialize<MessageHeader>(stream1);
			}
			
			
			if (header.MessageFormatVersion != MessageHeader.Schema2DataFormat)
				throw new InvalidOperationException("Unexpected message format");


			Schema2EnvelopeContract envelope;
			using (var stream = new MemoryStream(buffer, MessageHeader.FixedSize, (int) header.AttributesLength))
			{
				envelope = Serializer.Deserialize<Schema2EnvelopeContract>(stream);
			}
			int index = MessageHeader.FixedSize + (int) header.AttributesLength;
			//var count = (int)header.ContentLength;

			var items = new MessageItem[envelope.Items.Length];

			for (int i = 0; i < items.Length; i++)
			{
				Schema2ItemContract itemContract = envelope.Items[i];
				Maybe<Type> type = serializer.GetTypeByContractName(itemContract.ContractName);
				IDictionary<string, object> attributes = AttributesFromContract(itemContract.Attributes);

				if (type.HasValue)
				{
					using (var stream = new MemoryStream(buffer, index, itemContract.ContentSize))
					{
						object instance = serializer.Deserialize(stream, type.Value);

						items[i] = new MessageItem(type.Value, instance, attributes);
					}
				}
				else
				{
					// we can't deserialize. Keep it as buffer
					var bufferInstance = new byte[itemContract.ContentSize];
					Buffer.BlockCopy(buffer, index, bufferInstance, 0, itemContract.ContentSize);
					items[i] = new MessageItem(null, bufferInstance, attributes);
				}

				index += itemContract.ContentSize;
			}

			IDictionary<string, object> envelopeAttributes = AttributesFromContract(envelope.EnvelopeAttributes);
			return new MessageEnvelope(envelope.EnvelopeId, envelopeAttributes, items, envelope.DeliverOnUtc);
		}

		static byte[] SaveData(MessageEnvelope envelope, IMessageSerializer serializer)
		{
			//  string contract, Guid messageId, Uri sender, 
			var itemContracts = new Schema2ItemContract[envelope.Items.Length];
			using (var content = new MemoryStream())
			{
				int position = 0;
				for (int i = 0; i < envelope.Items.Length; i++)
				{
					MessageItem item = envelope.Items[i];
					string name = serializer.GetContractNameByType(item.MappedType)
						.ExposeException("Failed to find contract name for {0}", item.MappedType);
					serializer.Serialize(item.Content, content);
					int size = (int) content.Position - position;
					Schema2ItemAttributeContract[] attribContracts = ItemAttributesToContract(item.GetAllAttributes());
					itemContracts[i] = new Schema2ItemContract(name, size, attribContracts);

					position += size;
				}

				Schema2EnvelopeAttributeContract[] envelopeAttribs = EnvelopeAttributesToContract(envelope.GetAllAttributes());


				var contract = new Schema2EnvelopeContract(envelope.EnvelopeId, envelopeAttribs, itemContracts, envelope.DeliverOn);

				using (var stream = new MemoryStream())
				{
					// skip header
					stream.Seek(MessageHeader.FixedSize, SeekOrigin.Begin);
					// save envelope attributes
					Serializer.Serialize(stream, contract);
					long attributesLength = stream.Position - MessageHeader.FixedSize;
					// copy data
					content.WriteTo(stream);
					// write the header
					stream.Seek(0, SeekOrigin.Begin);
					MessageHeader messageHeader = MessageHeader.ForSchema2Data(attributesLength, content.Position);
					Serializer.Serialize(stream, messageHeader);
					return stream.ToArray();
				}
			}
		}
	}
}