using System;

namespace CacheManager.GenericKeys.Demo
{
    using System.Collections.Concurrent;

    using CacheManager.Core;

    using FluentAssertions;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;

    using CacheManager.Core.Internal;
    using CacheManager.Serialization.Protobuf.Pooled;
    using CacheManager.SQLite;

    using Microsoft.IO;

    class Program
    {
        static void Main(string[] args)
        {
            //SimpleValidation();

            for (int i = 0; i < 5; i++)
            PerformanceProfiling();
        }

        private static void PerformanceProfiling()
        {
            // Create cache
            SQLiteCacheHandleAdditionalConfiguration.BeginTransaction beginTransaction = null;
            using ICacheManager<int> cacheManager = CacheFactory.Build<int>(
                settings => settings
                    .WithProtoBufSerializer(new RecyclableMemoryStreamManager()) // TODO: Even the memory stream objects is causing perf problems, since it's in the tight loop', is there a way to pool those too?
                    .WithSQLiteCacheHandle(new SQLiteCacheHandleAdditionalConfiguration
                    {
                        DatabaseFilePath = "MyDatabase.sqlite",
                        SaveBeginTransactionMethod = method => beginTransaction = method
                    })
                    .Build());
            using GenericCache<Guid, int> toStringCache = new GenericCache<Guid, int>(cacheManager);

            toStringCache.Clear();

            // Performance profiling
            var st = Stopwatch.StartNew();
            try
            {
                // Use a transaction to avoid going to disk for EACH operation
                // TODO: Better/more ephemeral/auto ways to do this?
                using (var tr = beginTransaction())
                {
                    for (int i = 0; i < 28000; i++)
                    {
                        Guid newGuid = Guid.NewGuid();
                        toStringCache[newGuid] = i;

                        var cacheItemIfExists = toStringCache.GetCacheItem(newGuid);
                        cacheItemIfExists.Should().NotBeNull("should exist");
                        cacheItemIfExists.Value.value.Should().Be(i);
                    }

                    tr.Commit();
                    tr.Dispose();
                }
            }
            finally
            {
                Console.WriteLine(st.Elapsed);
            }
        }

        private static void SimpleValidation()
        {
            // Create cache
            GenericCache<long, string> toStringCache = new GenericCache<long, string>(
                CacheFactory.Build<string>(
                    settings => settings
                        .WithProtoBufSerializer()
                        .WithSQLiteCacheHandle(new SQLiteCacheHandleAdditionalConfiguration { DatabaseFilePath = "MyDatabase.sqlite" })
                        .Build()));

            // Initial state
            toStringCache.Exists(1).Should().BeFalse();

            // Add
            toStringCache.Add(1, "1").Should().BeTrue();
            toStringCache.Exists(1).Should().BeTrue();

            // Get
            toStringCache.Get(1).Should().Be("1");
        }
    }
}
