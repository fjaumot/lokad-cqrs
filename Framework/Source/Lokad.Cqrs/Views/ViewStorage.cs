﻿#region (c) 2010 Lokad Open Source - New BSD License 

// Copyright (c) Lokad 2010, http://www.lokad.com
// This code is released as Open Source under the terms of the New BSD Licence

#endregion

using System;
using System.Runtime.Serialization;
using Lokad.Cqrs.Storage;
using Lokad.Quality;
using Lokad.Serialization;

namespace Lokad.Cqrs.Views
{
	[UsedImplicitly]
	public sealed class ViewStorage : IStateStore
	{
		readonly IStorageContainer _container;
		readonly IDataSerializer _serializer;
		readonly IDataContractMapper _mapper;

		public ViewStorage(IStorageContainer container, IDataSerializer serializer, IDataContractMapper mapper)
		{
			_container = container;
			_serializer = serializer;
			_mapper = mapper;
		}

		IStorageItem MapTypeAndIdentity(Type type, string identity)
		{
			var value = _mapper
				.GetContractNameByType(type)
				.ExposeException(
					() => Errors.InvalidOperation("Type '{0}' should have contract name defined in '{1}'.", type, _mapper.GetType()));

			var name = string.Format("{0}-{1}.view", value, identity).ToLowerInvariant();
			return _container.GetItem(name);
		}

		public Maybe<object> Load(Type type, string identity)
		{
			var storage = MapTypeAndIdentity(type, identity);
			try
			{
				object loaded = null;
				storage.ReadInto((props, stream) => loaded = _serializer.Deserialize(stream, type));
				return loaded;
			}
			catch (StorageItemNotFoundException)
			{
				return Maybe<object>.Empty;
			}
		}

		public void Patch(Type type, string identity, Action<object> patch)
		{
			var item = MapTypeAndIdentity(type, identity);

			object source = null;
			StorageItemInfo info = null;

			item.ReadInto((props, stream) =>
				{
					source = _serializer.Deserialize(stream, type);
					info = props;
				});

			if (null == source)
				return; // there's nothing to patch

			patch(source);


			var match = StorageCondition.IfMatch(info.ETag);

			// if we fail condition, then this means, that
			// there was a concurrency problem
			try
			{
				item.Write(stream => _serializer.Serialize(source, stream), match);
			}
			catch (StorageConditionFailedException ex)
			{
				var msg = string.Format("Record was modified concurrently: '{0}'; Id: '{1}'. Please, retry.", type, identity);
				throw new OptimisticConcurrencyException(msg, ex);
			}
		}


		public void Delete(Type type, string identity)
		{
			MapTypeAndIdentity(type, identity).Delete();
		}

		public void Write(Type type, string identity, object item)
		{
			var storage = MapTypeAndIdentity(type, identity);
			storage.Write(stream => _serializer.Serialize(item,stream));
		}
	}

	[Serializable]
	public class OptimisticConcurrencyException : Exception
	{
		//
		// For guidelines regarding the creation of new exception types, see
		//    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
		// and
		//    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
		//

		public OptimisticConcurrencyException()
		{
		}

		public OptimisticConcurrencyException(string message) : base(message)
		{
		}

		public OptimisticConcurrencyException(string message, Exception inner) : base(message, inner)
		{
		}

		protected OptimisticConcurrencyException(
			SerializationInfo info,
			StreamingContext context) : base(info, context)
		{
		}
	}

	public static class ExtendIViewStorage
	{
		public static void Write<T>(this IWriteState store, string identity, T item)
		{
			store.Write(typeof(T), identity, item);
		}

		public static Maybe<T> Load<T>(this IReadState store, string identity)
		{
			return store.Load(typeof (T), identity).Convert(o => (T) o);
		}

		public static void Patch<T>(this IWriteState store, string identity, Action<T> patch)
		{
			store.Patch(typeof (T), identity, obj => patch((T) obj));
		}

		public static void Delete<T>(this IWriteState store, string identity)
		{
			store.Delete(typeof(T), identity);
		}
	}
}