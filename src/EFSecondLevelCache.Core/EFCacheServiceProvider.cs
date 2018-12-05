using System;
using System.Collections.Generic;
using System.Threading;
using CacheManager.Core;
using EFSecondLevelCache.Core.Contracts;

namespace EFSecondLevelCache.Core
{
    /// <summary>
    /// Using ICacheManager as a cache service.
    /// </summary>
    public class EFCacheServiceProvider : IEFCacheServiceProvider
    {
        private static readonly EFCacheKey _nullObject = new EFCacheKey();
        private readonly ICacheManager<ISet<string>> _dependenciesCacheManager;
        private readonly ICacheManager<object> _valuesCacheManager;
        private readonly ReaderWriterLockSlim _vcmReaderWriterLock = new ReaderWriterLockSlim();
        private readonly ReaderWriterLockSlim _dcReaderWriterLock = new ReaderWriterLockSlim();

        /// <summary>
        /// Some cache providers won't accept null values.
        /// So we need a custom Null object here. It should be defined `static readonly` in your code.
        /// </summary>
        public object NullObject => _nullObject;

        /// <summary>
        /// Using ICacheManager as a cache service.
        /// </summary>
        public EFCacheServiceProvider(
            ICacheManager<object> valuesCacheManager,
            ICacheManager<ISet<string>> dependenciesCacheManager)
        {
            _valuesCacheManager = valuesCacheManager;
            _dependenciesCacheManager = dependenciesCacheManager;
        }

        /// <summary>
        /// Removes the cached entries added by this library.
        /// </summary>
        public void ClearAllCachedEntries()
        {
            TryWriteLocked(_vcmReaderWriterLock, () => _valuesCacheManager.Clear());
            TryWriteLocked(_dcReaderWriterLock, () => _dependenciesCacheManager.Clear());
        }

        /// <summary>
        /// Gets a cached entry by key.
        /// </summary>
        /// <param name="cacheKey">key to find</param>
        /// <returns>cached value</returns>
        public object GetValue(string cacheKey)
        {
            return _valuesCacheManager.Get(cacheKey);
        }

        /// <summary>
        /// Adds a new item to the cache.
        /// </summary>
        /// <param name="cacheKey">key</param>
        /// <param name="value">value</param>
        /// <param name="rootCacheKeys">cache dependencies</param>
        public void InsertValue(string cacheKey, object value,
                                ISet<string> rootCacheKeys)
        {
            if (value == null)
            {
                value = NullObject; // `HttpRuntime.Cache.Insert` won't accept null values.
            }

            foreach (var rootCacheKey in rootCacheKeys)
            {
                TryWriteLocked(_dcReaderWriterLock, () =>
                {
                    _dependenciesCacheManager.AddOrUpdate(rootCacheKey, new HashSet<string> { cacheKey },
                        updateValue: set =>
                        {
                            set.Add(cacheKey);
                            return set;
                        });
                });
            }

            TryWriteLocked(_vcmReaderWriterLock, () => _valuesCacheManager.Add(cacheKey, value));
        }

        /// <summary>
        /// Invalidates all of the cache entries which are dependent on any of the specified root keys.
        /// </summary>
        /// <param name="rootCacheKeys">cache dependencies</param>
        public void InvalidateCacheDependencies(string[] rootCacheKeys)
        {
            foreach (var rootCacheKey in rootCacheKeys)
            {
                if (string.IsNullOrWhiteSpace(rootCacheKey))
                {
                    continue;
                }

                clearDependencyValues(rootCacheKey);
                TryWriteLocked(_dcReaderWriterLock, () => _dependenciesCacheManager.Remove(rootCacheKey));
            }
        }

        private void clearDependencyValues(string rootCacheKey)
        {
           TryReadLocked(_dcReaderWriterLock, () =>
           {
               var dependencyKeys = _dependenciesCacheManager.Get(rootCacheKey);
               if (dependencyKeys == null)
               {
                   return;
               }

               foreach (var dependencyKey in dependencyKeys)
               {
                   TryWriteLocked(_vcmReaderWriterLock, () => _valuesCacheManager.Remove(dependencyKey));
               }
           });
        }

        private static void TryReadLocked(ReaderWriterLockSlim readerWriterLock, Action action, int timeout = Timeout.Infinite)
        {
            if (!readerWriterLock.TryEnterReadLock(timeout))
            {
                throw new TimeoutException();
            }
            try
            {
                action();
            }
            finally
            {
                readerWriterLock.ExitReadLock();
            }
        }

        private static void TryWriteLocked(ReaderWriterLockSlim readerWriterLock, Action action, int timeout = Timeout.Infinite)
        {
            if (!readerWriterLock.TryEnterWriteLock(timeout))
            {
                throw new TimeoutException();
            }
            try
            {
                action();
            }
            finally
            {
                readerWriterLock.ExitWriteLock();
            }
        }
    }
}