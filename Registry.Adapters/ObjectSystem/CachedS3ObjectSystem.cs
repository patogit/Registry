﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Registry.Adapters.ObjectSystem.Model;
using Registry.Common;
using Registry.Common.Model;
using Registry.Ports.ObjectSystem;
using Registry.Ports.ObjectSystem.Model;
using Exception = System.Exception;

namespace Registry.Adapters.ObjectSystem
{
    public class CachedS3ObjectSystem : IObjectSystem
    {
        private readonly ILogger<CachedS3ObjectSystem> _logger;
        private readonly S3ObjectSystem _remoteStorage;

        public string CachePath { get; }
        public TimeSpan CacheExpiration { get; }
        public long? MaxSize { get; }

        private const string CacheFileEntryFormat = "{0}-{1}";
        private const string CacheFileInfoFormat = "{0}-{1}.json";

        private string GetCacheFileName(string bucketName, string objectName)
        {
            return Path.Combine(CachePath, bucketName, objectName.Replace('/', '-'));
        }
        private string GetCacheFileInfoName(string bucketName, string objectName)
        {
            return Path.Combine(CachePath, bucketName, objectName.Replace('/', '-') + ".json");
        }

        public CachedS3ObjectSystem(
            CachedS3ObjectSystemSettings settings, ILogger<CachedS3ObjectSystem> logger)

        {
            _logger = logger;
            CachePath = settings.CachePath;
            CacheExpiration = settings.CacheExpiration;
            MaxSize = settings.MaxSize;

            _remoteStorage = new S3ObjectSystem(settings);
        }

        public async Task GetObjectAsync(string bucketName, string objectName, Action<Stream> callback, IServerEncryption sse = null,
            CancellationToken cancellationToken = default)
        {

            var cachedFileName = GetCacheFileName(bucketName, objectName);

            try
            {
                if (File.Exists(cachedFileName))
                {
                    var memory = new MemoryStream();
                    await using (var s = File.OpenRead(cachedFileName))
                        await s.CopyToAsync(memory, cancellationToken);

                    memory.Reset();

                    callback(memory);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Cannot read from cache the file '{cachedFileName}' in '{bucketName}' bucket and '{objectName}' object");
            }

            await _remoteStorage.GetObjectAsync(bucketName, objectName, stream =>
            {
                using var memory = new MemoryStream();
                stream.CopyTo(memory);

                try
                {
                    EnsureBucketPathExists(bucketName);
                    memory.Reset();
                    using var s = File.OpenWrite(cachedFileName);
                    memory.CopyTo(s);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Cannot write to cache the file '{cachedFileName}' in '{bucketName}' bucket and '{objectName}' object");
                }

                memory.Reset();

                callback(memory);
            }, sse, cancellationToken);
        }

        private void EnsureBucketPathExists(string bucketName)
        {
            var bucketPath = GetBucketFolder(bucketName);
            Directory.CreateDirectory(bucketPath);

        }

        public async Task GetObjectAsync(string bucketName, string objectName, long offset, long length, Action<Stream> cb,
            IServerEncryption sse = null, CancellationToken cancellationToken = default)
        {
            // NOTE: Not supported partial file recovery
            await _remoteStorage.GetObjectAsync(bucketName, objectName, offset, length, cb, sse, cancellationToken);
        }


        public async Task GetObjectAsync(string bucketName, string objectName, string filePath, IServerEncryption sse = null,
            CancellationToken cancellationToken = default)
        {

            var cachedFileName = GetCacheFileName(bucketName, objectName);

            try
            {
                if (File.Exists(cachedFileName))
                {
                    File.Copy(cachedFileName, filePath, true);
                    return;
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Cannot read from cache the file '{cachedFileName}' in '{bucketName}' bucket and '{objectName}' object");
            }

            await _remoteStorage.GetObjectAsync(bucketName, objectName, filePath, sse, cancellationToken);

            try
            {
                EnsureBucketPathExists(bucketName);

                File.Copy(filePath, cachedFileName, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Cannot copy to cache from '{filePath}' to '{cachedFileName}' in '{bucketName}' bucket and '{objectName}' object");
            }

        }


        public async Task<ObjectInfo> GetObjectInfoAsync(string bucketName, string objectName, IServerEncryption sse = null,
            CancellationToken cancellationToken = default)
        {

            var cachedFileInfoName = GetCacheFileInfoName(bucketName, objectName);

            try
            {
                if (File.Exists(cachedFileInfoName))
                {
                    return JsonConvert.DeserializeObject<ObjectInfo>(
                        await File.ReadAllTextAsync(cachedFileInfoName, cancellationToken));
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Cannot read from cache the file info '{cachedFileInfoName}' in '{bucketName}' bucket and '{objectName}' object");
            }


            var objectInfo = await _remoteStorage.GetObjectInfoAsync(bucketName, objectName, sse, cancellationToken);

            try
            {
                EnsureBucketPathExists(bucketName);

                await File.WriteAllTextAsync(cachedFileInfoName, JsonConvert.SerializeObject(objectInfo, Formatting.Indented), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Cannot write file info to cache '{cachedFileInfoName}' in '{bucketName}' bucket and '{objectName}' object");
            }

            return objectInfo;
        }

        public IObservable<ObjectUpload> ListIncompleteUploads(string bucketName, string prefix = "", bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            return _remoteStorage.ListIncompleteUploads(bucketName, prefix, recursive, cancellationToken);
        }

        public async Task RemoveIncompleteUploadAsync(string bucketName, string objectName, CancellationToken cancellationToken = default)
        {
            await _remoteStorage.RemoveIncompleteUploadAsync(bucketName, objectName, cancellationToken);
        }

        public async Task CopyObjectAsync(string bucketName, string objectName, string destBucketName, string destObjectName = null,
            IReadOnlyDictionary<string, string> copyConditions = null, Dictionary<string, string> metadata = null, IServerEncryption sseSrc = null,
            IServerEncryption sseDest = null, CancellationToken cancellationToken = default)
        {
            await _remoteStorage.CopyObjectAsync(bucketName, objectName, destBucketName, destObjectName, copyConditions, metadata, sseSrc, sseDest, cancellationToken);
        }

        #region Put

        public async Task PutObjectAsync(string bucketName, string objectName, Stream data, long size, string contentType = null,
            Dictionary<string, string> metaData = null, IServerEncryption sse = null, CancellationToken cancellationToken = default)
        {

            await _remoteStorage.PutObjectAsync(bucketName, objectName, data, size, contentType, metaData, sse, cancellationToken);

            var cachedFileName = GetCacheFileName(bucketName, objectName);

            try
            {
                data.Reset();

                EnsureBucketPathExists(bucketName);

                await using var writer = File.OpenWrite(cachedFileName);
                await data.CopyToAsync(writer, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Cannot write to cache the file '{cachedFileName}' in '{bucketName}' bucket and '{objectName}' object");
            }

        }

        public async Task PutObjectAsync(string bucketName, string objectName, string filePath, string contentType = null,
            Dictionary<string, string> metaData = null, IServerEncryption sse = null, CancellationToken cancellationToken = default)
        {

            await _remoteStorage.PutObjectAsync(bucketName, objectName, filePath, contentType, metaData, sse, cancellationToken);

            var cachedFileName = GetCacheFileName(bucketName, objectName);
            try
            {
                EnsureBucketPathExists(bucketName);

                File.Copy(filePath, cachedFileName, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Cannot copy to cache from '{filePath}' to '{cachedFileName}' in '{bucketName}' bucket and '{objectName}' object");
            }

        }

        #endregion

        public async Task MakeBucketAsync(string bucketName, string location = null, CancellationToken cancellationToken = default)
        {
            await _remoteStorage.MakeBucketAsync(bucketName, location, cancellationToken);
        }

        public async Task<ListBucketsResult> ListBucketsAsync(CancellationToken cancellationToken = default)
        {
            return await _remoteStorage.ListBucketsAsync(cancellationToken);
        }

        public async Task<bool> BucketExistsAsync(string bucketName, CancellationToken cancellationToken = default)
        {
            return await _remoteStorage.BucketExistsAsync(bucketName, cancellationToken);
        }

        public async Task RemoveObjectAsync(string bucketName, string objectName, CancellationToken cancellationToken = default)
        {

            var cachedFileName = GetCacheFileName(bucketName, objectName);

            try
            {
                if (File.Exists(cachedFileName)) File.Delete(cachedFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    $"Cannot delete cached file '{cachedFileName}' in '{bucketName}' bucket and '{objectName}' object");
            }

            await _remoteStorage.RemoveObjectAsync(bucketName, objectName, cancellationToken);
            
        }

        public async Task RemoveBucketAsync(string bucketName, bool force = true, CancellationToken cancellationToken = default)
        {

            var bucketFolder = GetBucketFolder(bucketName);
            try
            {
                if (Directory.Exists(bucketFolder)) Directory.Delete(bucketFolder, true);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    $"Cannot delete cached files in folder '{bucketFolder}' of '{bucketName}' bucket and");
            }

            await _remoteStorage.RemoveBucketAsync(bucketName, force, cancellationToken);
            
        }

        private string GetBucketFolder(string bucketName)
        {
            return Path.GetFullPath(Path.Combine(CachePath, bucketName));
        }

        public IObservable<ItemInfo> ListObjectsAsync(string bucketName, string prefix = null, bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            return _remoteStorage.ListObjectsAsync(bucketName, prefix, recursive, cancellationToken);
        }

        public async Task<string> GetPolicyAsync(string bucketName, CancellationToken cancellationToken = default)
        {
            return await _remoteStorage.GetPolicyAsync(bucketName, cancellationToken);
        }

        public async Task SetPolicyAsync(string bucketName, string policyJson,
            CancellationToken cancellationToken = default)
        {
            await _remoteStorage.SetPolicyAsync(bucketName, policyJson, cancellationToken);
        }

        public StorageInfo GetStorageInfo()
        {
            return _remoteStorage.GetStorageInfo();
        }
    }
}
