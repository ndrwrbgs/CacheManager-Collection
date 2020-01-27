
namespace CacheManager.SQLite {
    using System;

    using CacheManager.Core;

    public static class SQLiteCacheHandleConfigurationBuilderExtensions
    {
        public static ConfigurationBuilderCacheHandlePart WithSQLiteCacheHandle(
            this ConfigurationBuilderCachePart part,
            SQLiteCacheHandleAdditionalConfiguration config)
            => part?.WithHandle(
                typeof(SQLiteCacheHandle<>),
                Guid.NewGuid().ToString(),
                isBackplaneSource: false,
                config);
    }
}