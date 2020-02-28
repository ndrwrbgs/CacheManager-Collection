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
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

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
            var sqliteConfig = new SQLiteCacheHandleAdditionalConfiguration
            {
                DatabaseFilePath = "MyDatabase2.sqlite"
            };
            using ICacheManager<int> cacheManager = CacheFactory.Build<int>(
                settings =>
                {
                    settings
                        .WithProtoBufSerializer(
                            new RecyclableMemoryStreamManager()) // TODO: Even the memory stream objects is causing perf problems, since it's in the tight loop', is there a way to pool those too?
                        .WithSQLiteCacheHandle(sqliteConfig)
                        .EnableStatistics()
                        .Build();
                });
            using CancellationTokenSource cts = new CancellationTokenSource();
            Task.Run(
                async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(100);

                        var stats = cacheManager.CacheHandles.First().Stats;
                        Console.WriteLine(
                            string.Format(
                                "Items: {0}, Hits: {1}, Miss: {2}, Remove: {3}, ClearRegion: {4}, Clear: {5}, Adds: {6}, Puts: {7}, Gets: {8}",
                                stats.GetStatistic(CacheStatsCounterType.Items),
                                stats.GetStatistic(CacheStatsCounterType.Hits),
                                stats.GetStatistic(CacheStatsCounterType.Misses),
                                stats.GetStatistic(CacheStatsCounterType.RemoveCalls),
                                stats.GetStatistic(CacheStatsCounterType.ClearRegionCalls),
                                stats.GetStatistic(CacheStatsCounterType.ClearCalls),
                                stats.GetStatistic(CacheStatsCounterType.AddCalls),
                                stats.GetStatistic(CacheStatsCounterType.PutCalls),
                                stats.GetStatistic(CacheStatsCounterType.GetCalls)
                            ));
                    }
                });
            using GenericCache<Guid, int> toStringCache = new GenericCache<Guid, int>(cacheManager);

            toStringCache.Clear();

            // Performance profiling
            var st = Stopwatch.StartNew();
            try
            {
                // Use a transaction to avoid going to disk for EACH operation
                // TODO: Better/more ephemeral/auto ways to do this?
                using (var tr = sqliteConfig.GetBeginTransactionMethod()())
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
