﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFSecondLevelCache.Core.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFSecondLevelCache.Core.Tests
{
    [TestClass]
    public class EFCacheServiceProviderTests
    {
        private readonly IEFCacheServiceProvider _cacheService;
        public EFCacheServiceProviderTests()
        {
            _cacheService = TestsBase.GetInMemoryCacheServiceProvider();
            //_cacheService = TestsBase.GetRedisCacheServiceProvider();
        }

        [TestInitialize]
        public void ClearEFGlobalCacheBeforeEachTest()
        {
            _cacheService.ClearAllCachedEntries();
        }

        [TestMethod]
        public void TestCacheInvalidationWithTwoRoots()
        {
            _cacheService.InsertValue("EF_key1", "value1", new HashSet<string> { "entity1.model", "entity2.model" });

            _cacheService.InsertValue("EF_key2", "value2", new HashSet<string> { "entity1.model", "entity2.model" });


            var value1 = _cacheService.GetValue("EF_key1");
            Assert.IsNotNull(value1);

            var value2 = _cacheService.GetValue("EF_key2");
            Assert.IsNotNull(value2);

            _cacheService.InvalidateCacheDependencies(new[] { "entity2.model" });

            value1 = _cacheService.GetValue("EF_key1");
            Assert.IsNull(value1);

            value2 = _cacheService.GetValue("EF_key2");
            Assert.IsNull(value2);
        }

        [TestMethod]
        public void TestCacheInvalidationWithOneRoot()
        {
            _cacheService.InsertValue("EF_key1", "value1", new HashSet<string> { "entity1" });

            _cacheService.InsertValue("EF_key2", "value2", new HashSet<string> { "entity1" });

            var value1 = _cacheService.GetValue("EF_key1");
            Assert.IsNotNull(value1);

            var value2 = _cacheService.GetValue("EF_key2");
            Assert.IsNotNull(value2);

            _cacheService.InvalidateCacheDependencies(new[] { "entity1" });

            value1 = _cacheService.GetValue("EF_key1");
            Assert.IsNull(value1);

            value2 = _cacheService.GetValue("EF_key2");
            Assert.IsNull(value2);
        }

        [TestMethod]
        public void TestCacheInvalidationWithSimilarRoots()
        {
            _cacheService.InsertValue("EF_key1", "value1", new HashSet<string> { "entity1", "entity2" });

            _cacheService.InsertValue("EF_key2", "value2", new HashSet<string> { "entity2" });

            var value1 = _cacheService.GetValue("EF_key1");
            Assert.IsNotNull(value1);

            var value2 = _cacheService.GetValue("EF_key2");
            Assert.IsNotNull(value2);

            _cacheService.InvalidateCacheDependencies(new[] { "entity2" });

            value1 = _cacheService.GetValue("EF_key1");
            Assert.IsNull(value1);

            value2 = _cacheService.GetValue("EF_key2");
            Assert.IsNull(value2);
        }

        [TestMethod]
        public void TestInsertingNullValues()
        {
            _cacheService.InsertValue("EF_key1", null, new HashSet<string> { "entity1", "entity2" });

            var value1 = _cacheService.GetValue("EF_key1");
            Assert.IsTrue(Equals(value1, _cacheService.NullObject), $"value1 is `{value1}`");
        }

        [TestMethod]
        public void TestParallelInsertsAndRemoves()
        {
            var tests = new List<Action>();

            for (var i = 0; i < 4000; i++)
            {
                var i1 = i;
                tests.Add(() => _cacheService.InsertValue($"EF_key{i1}", i1, new HashSet<string> {"entity1", "entity2"}));
            }

            for (var i = 0; i < 400; i++)
            {
                if (i % 2 == 0)
                {
                    tests.Add(() => _cacheService.InvalidateCacheDependencies(new[] { "entity1" }));
                }
                else
                {
                    tests.Add(() => _cacheService.InvalidateCacheDependencies(new[] { "entity2" }));
                }
            }

            var rnd = new Random();
            Parallel.Invoke(tests.OrderBy(a => rnd.Next()).ToArray());

            var value1 = _cacheService.GetValue("EF_key1");
            Assert.IsNull(value1);
        }
    }
}