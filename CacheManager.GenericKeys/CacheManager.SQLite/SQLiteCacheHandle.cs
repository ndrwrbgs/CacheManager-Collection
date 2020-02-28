
namespace CacheManager.SQLite
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.SQLite;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using CacheManager.Core;
    using CacheManager.Core.Internal;
    using CacheManager.Core.Logging;

    [RequiresSerializer]
    public class SQLiteCacheHandle<TCacheValue> : BaseCacheHandle<TCacheValue>
    {
        private readonly ICacheSerializer serializer;
        private readonly SQLiteCacheHandleAdditionalConfiguration additionalConfiguration;

        /// <summary>
        /// From http://cachemanager.michaco.net/documentation/CacheManagerConfiguration
        /// The cache or cache handle name in general is optional. It can be used for debugging/logging purposes though
        /// </summary>
        private string cacheName;

        private SQLiteConnection conn;

        public SQLiteCacheHandle(
            ICacheManagerConfiguration managerConfiguration,
            CacheHandleConfiguration configuration,
            ILoggerFactory loggerFactory,
            ICacheSerializer serializer,
            SQLiteCacheHandleAdditionalConfiguration additionalConfiguration)
            : base(managerConfiguration, configuration)
        {
            this.serializer = serializer;
            this.additionalConfiguration = additionalConfiguration ?? new SQLiteCacheHandleAdditionalConfiguration();

            Logger = loggerFactory.CreateLogger(this);

            this.cacheName = configuration.Name;

            this.conn = CreateConnection(this.additionalConfiguration.DatabaseFilePath);

            if (additionalConfiguration != null)
            {
                additionalConfiguration.BeginTransactionMethod = () => this.conn.BeginTransaction( /* TODO: Support arguments/overloads */);
            }

            this.SetInitialItemCount();

            RemoveExpiredItems();
        }

        private void SetInitialItemCount()
        {
            // TODO: We definitely cannot use this as-is as a non-breaking change in CacheManager.Core would break us without even usable exceptions

            var isStatsEnabledField = this.Stats.GetType().GetField("_isStatsEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            var isStatsEnabled = isStatsEnabledField.GetValue(this.Stats);
            if (!(bool) isStatsEnabled)
            {
                return;
            }

            // TODO: We don't want this invalid value added to the stats, but it's easier than trying to create a new CacheStatsCounter when default hasn't been added yet
            this.Stats.OnHit();

            var cacheStats_TCacheValue = this.Stats.GetType();
            var countersField = cacheStats_TCacheValue.GetField("_counters", BindingFlags.NonPublic | BindingFlags.Instance);
            var counters = /* ConcurrentDictionary<string, CacheStatsCounter> */ countersField.GetValue(this.Stats);
            var cacheStatsCounter = counters.GetType().GenericTypeArguments.ElementAt(1);
            var dictionary = typeof(IDictionary<,>).MakeGenericType(typeof(string), cacheStatsCounter);
            var dictionaryIndexer = dictionary
                .GetProperties()
                .Single(p => p.GetIndexParameters().Any());
            var nullRegionKey = GetNullRegionKey();
            var cacheStatsCounterValue = /* CacheStatsCounter */ dictionaryIndexer.GetValue(counters, new object[] { nullRegionKey });

            var setMethod = cacheStatsCounter.GetMethod("Set");
            int initialCount = GetInitialCount();
            setMethod.Invoke(cacheStatsCounterValue, new object[] {CacheStatsCounterType.Items, initialCount });

            string GetNullRegionKey()
            {
                var nullRegionKeyField = typeof(CacheStats<TCacheValue>).GetField("_nullRegionKey", BindingFlags.NonPublic | BindingFlags.Static);
                return (string) nullRegionKeyField.GetValue(null);
            }

            int GetInitialCount()
            {
                using var sqLiteCommand = new SQLiteCommand(
                    $"SELECT COUNT(*) FROM entries WHERE exp > @exp",
                    this.conn);
                sqLiteCommand.Parameters.AddWithValue("@exp", DateTimeOffset.UtcNow.Ticks);
                long count = (long)sqLiteCommand.ExecuteScalar();
                return (int)count;
            }
        }

        private static SQLiteConnection CreateConnection(string databaseFilePath)
        {
            if (!File.Exists(databaseFilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(databaseFilePath));
                SQLiteConnection.CreateFile(databaseFilePath);
            }

            var sqLiteConnection = new SQLiteConnection("Data Source=" + databaseFilePath);
            sqLiteConnection.Open();
            //new SQLiteCommand("PRAGMA synchronous = OFF", sqLiteConnection).ExecuteNonQuery();
            //new SQLiteCommand("PRAGMA journal_mode = MEMORY", sqLiteConnection).ExecuteNonQuery();

            // Create tables if needed

            string create = "CREATE TABLE IF NOT EXISTS entries "
                            + "( key TEXT PRIMARY KEY, val BLOB, exp FLOAT )";
            string createIndex = "CREATE INDEX IF NOT EXISTS keyname_index ON entries (key)";

            using var c1 = new SQLiteCommand(create, sqLiteConnection);
            c1.ExecuteNonQuery();
            using var c2 = new SQLiteCommand(createIndex, sqLiteConnection);
            c2.ExecuteNonQuery();

            return sqLiteConnection;
        }

        protected override void Dispose(bool disposeManaged)
        {
            base.Dispose(disposeManaged);

            var toDispose = Interlocked.Exchange(ref this.conn, null);
            if (toDispose != null)
            {
                toDispose.Close();
                toDispose.Dispose();
            }
        }

        public override void Clear()
        {
            using var c = new SQLiteCommand("DELETE FROM entries", this.conn);
            c.ExecuteNonQuery();
        }

        public override void ClearRegion(string region)
        {
            throw new NotSupportedException("No support for region yet");
        }

        public override bool Exists(string key)
        {
            using var sqLiteCommand = new SQLiteCommand(
                $"SELECT COUNT(*) FROM entries WHERE key = @key AND exp > @exp",
                this.conn);
            sqLiteCommand.Parameters.AddWithValue("@key", key);
            sqLiteCommand.Parameters.AddWithValue("@exp", DateTimeOffset.UtcNow.Ticks);
            long count = (long)sqLiteCommand.ExecuteScalar();
            return count > 0;
        }

        public override bool Exists(string key, string region)
        {
            throw new NotSupportedException("No support for region yet");
        }

        protected override CacheItem<TCacheValue> GetCacheItemInternal(string key)
        {
            using var sqLiteCommand = new SQLiteCommand(
                $"SELECT val, exp FROM entries WHERE key = @key",
                this.conn);
            sqLiteCommand.Parameters.AddWithValue("@key", key);
            using var sqLiteDataReader = sqLiteCommand
                .ExecuteReader();

            if (!sqLiteDataReader.Read()) return null;

            var exp = sqLiteDataReader.GetFieldValue<double>(1);

            if (exp <= DateTimeOffset.UtcNow.Ticks)
            {
                // Delete the value that is expired
                RemoveExpiredItems();
                return null;
            }
            else
            {
                var valByteArray = sqLiteDataReader.GetFieldValue<byte[]>(0);
                var val = (TCacheValue) this.serializer.Deserialize(valByteArray, typeof(TCacheValue));
                return new CacheItem<TCacheValue>(key, val, ExpirationMode.Absolute, TimeSpan.FromDays(365));
            }
        }

        protected override CacheItem<TCacheValue> GetCacheItemInternal(string key, string region)
        {
            throw new NotSupportedException("No support for region yet");
        }

        protected override bool RemoveInternal(string key)
        {
            using var sqLiteCommand = new SQLiteCommand(
                // TODO: Will say it did delete an item, even if the item was already expired
                $"DELETE FROM entries WHERE key = @key",
                this.conn);
            sqLiteCommand.Parameters.AddWithValue("@key", key);
            int rowsAffected = sqLiteCommand
                .ExecuteNonQuery();

            return rowsAffected != 0;
        }

        private void RemoveExpiredItems()
        {
            using var sqLiteCommand = new SQLiteCommand(
                $"DELETE FROM entries WHERE exp < @exp",
                this.conn);
            sqLiteCommand.Parameters.AddWithValue("@exp", DateTimeOffset.UtcNow.Ticks);
            sqLiteCommand.ExecuteNonQuery();
        }

        protected override bool RemoveInternal(string key, string region)
        {
            throw new NotSupportedException("No support for region yet");
        }

        protected override ILogger Logger { get; }
        protected override bool AddInternalPrepared(CacheItem<TCacheValue> item)
        {
            var serializedValueBytes = this.serializer.Serialize(item.Value);

            using var sqLiteCommand = new SQLiteCommand(
                $"INSERT OR IGNORE INTO entries (key, val, exp)"
                + $" VALUES (@key, @val, @exp)",
                this.conn);
            sqLiteCommand.Parameters.AddWithValue("@key", item.Key);
            sqLiteCommand.Parameters.AddWithValue("@val", serializedValueBytes);
            sqLiteCommand.Parameters.AddWithValue("@exp", WhenShouldIExpire(item));
            var rowsUpdated = sqLiteCommand
                .ExecuteNonQuery();

            return rowsUpdated == 1;
        }

        private static long WhenShouldIExpire(CacheItem<TCacheValue> item)
        {
            return WhenShouldIExpireImpl(item).ToUniversalTime().Ticks;
        }


        private static DateTimeOffset WhenShouldIExpireImpl(CacheItem<TCacheValue> item)
        {
            switch (item.ExpirationMode)
            {
                case ExpirationMode.Default: // Documentation on Default says it defaults to None
                case ExpirationMode.None:
                    // An obnoxiously large value, since we don't have support for null implemented
                    return DateTimeOffset.Parse("1/1/9000");
                case ExpirationMode.Sliding:
                    // TODO: Not implemented, should be updating the LastAccessed on each read, but we don't do that now. Absolute is our approximation
                    return DateTimeOffset.UtcNow.Add(item.ExpirationTimeout);
                case ExpirationMode.Absolute:
                    return DateTimeOffset.UtcNow.Add(item.ExpirationTimeout);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected override void PutInternalPrepared(CacheItem<TCacheValue> item)
        {
            var serializedValueBytes = this.serializer.Serialize(item.Value);

            using var sqLiteCommand = new SQLiteCommand(
                $"REPLACE INTO entries (key, val, exp)"
                + $" VALUES (@key, @val, @exp)",
                this.conn);
            sqLiteCommand.Parameters.AddWithValue("@key", item.Key);
            sqLiteCommand.Parameters.AddWithValue("@val", serializedValueBytes);
            sqLiteCommand.Parameters.AddWithValue("@exp", WhenShouldIExpire(item));
            sqLiteCommand
                .ExecuteNonQuery();
        }

        public override int Count
        {
            get
            {
                using var sqLiteCommand = new SQLiteCommand(
                    // TODO: We should probably occasionally/background clean up items that are expired
                    "SELECT COUNT(*) FROM entries WHERE exp > @exp",
                    this.conn);
                sqLiteCommand.Parameters.AddWithValue("@exp", DateTimeOffset.UtcNow.Ticks);
                long count = (long) sqLiteCommand
                    .ExecuteScalar();
                return (int)count;
            }
        }
    }
}