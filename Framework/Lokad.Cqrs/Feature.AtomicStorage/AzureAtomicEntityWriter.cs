﻿#region (c) 2010-2011 Lokad - CQRS for Windows Azure - New BSD License 

// Copyright (c) Lokad 2010-2011, http://www.lokad.com
// This code is released as Open Source under the terms of the New BSD Licence

#endregion

using System;
using Microsoft.WindowsAzure.StorageClient;

namespace Lokad.Cqrs.Feature.AtomicStorage
{
    /// <summary>
    /// Azure implementation of the view reader/writer
    /// </summary>
    /// <typeparam name="TEntity">The type of the view.</typeparam>
    public sealed class AzureAtomicEntityWriter<TEntity> :
        IAtomicEntityWriter<TEntity>
        //where TEntity : IAtomicEntity<TKey>
    {
        readonly CloudBlobContainer _container;
        readonly IAzureAtomicStorageStrategy _convention;

        public AzureAtomicEntityWriter(CloudBlobClient client, IAzureAtomicStorageStrategy convention)
        {
            var containerName = _convention.GetFolderForEntity(typeof (TEntity));
            _container = client.GetContainerReference(containerName);
            _convention = convention;
        }

        public TEntity AddOrUpdate(object key, Func<TEntity> addViewFactory, Func<TEntity,TEntity> updateViewFactory, AddOrUpdateHint hint)
        {
            // TODO: implement proper locking and order
            var blob = GetBlobReference(key);
            TEntity view;
            try
            {
                var downloadText = blob.DownloadText();
                view = _convention.Deserialize<TEntity>(downloadText);
                view = updateViewFactory(view);
            }
            catch (StorageClientException ex)
            {
                view = addViewFactory();
            }

            blob.UploadText(_convention.Serialize(view));
            return view;
        }


        public bool TryDelete(object key)
        {
            var blob = GetBlobReference(key);
            return blob.DeleteIfExists();
        }

        CloudBlob GetBlobReference(object key)
        {
            var name =  _convention.GetNameForEntity(typeof(TEntity), key);
            return _container.GetBlobReference(name);
        }
    }
}