﻿
namespace CacheManager.SQLite
{
    using System;
    using System.Data;
    using System.Data.SQLite;
    using System.IO;
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

            RemoveExpiredItems();
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

            // Create tables if needed

            string create = "CREATE TABLE IF NOT EXISTS entries "
                            + "( key TEXT PRIMARY KEY, val BLOB, exp FLOAT )";
            string createIndex = "CREATE INDEX IF NOT EXISTS keyname_index ON entries (key)";

            new SQLiteCommand(create, sqLiteConnection).ExecuteNonQuery();
            new SQLiteCommand(createIndex, sqLiteConnection).ExecuteNonQuery();

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
            new SQLiteCommand("DELETE FROM entries", this.conn).ExecuteNonQuery();
        }

        public override void ClearRegion(string region)
        {
            throw new NotSupportedException("No support for region yet");
        }

        public override bool Exists(string key)
        {
            var sqLiteCommand = new SQLiteCommand(
                $"SELECT COUNT(*) FROM entries WHERE key = @key AND exp > @exp",
                this.conn);
            sqLiteCommand.Parameters.AddWithValue("@key", key);
            sqLiteCommand.Parameters.AddWithValue("@exp", DateTimeOffset.UtcNow.Ticks);
            int count = (int)sqLiteCommand.ExecuteScalar();
            return count > 0;
        }

        public override bool Exists(string key, string region)
        {
            throw new NotSupportedException("No support for region yet");
        }

        protected override CacheItem<TCacheValue> GetCacheItemInternal(string key)
        {
            var sqLiteCommand = new SQLiteCommand(
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
            var sqLiteCommand = new SQLiteCommand(
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
            var sqLiteCommand = new SQLiteCommand(
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
            // TODO: Can be optimized by doing all SQL side, but this is easier to write for now
            using (var tr = this.conn.BeginTransaction())
            {
                if (this.Exists(item.Key))
                {
                    return false;
                }

                var serializedValueBytes = this.serializer.Serialize(item.Value);

                var sqLiteCommand = new SQLiteCommand(
                    $"INSERT INTO entries (key, val, exp)"
                    + $" VALUES (@key, @val, @exp)",
                    this.conn);
                sqLiteCommand.Parameters.AddWithValue("@key", item.Key);
                sqLiteCommand.Parameters.AddWithValue("@val", serializedValueBytes);
                sqLiteCommand.Parameters.AddWithValue("@exp", WhenShouldIExpire(item));
                sqLiteCommand
                    .ExecuteNonQuery();

                tr.Commit();
                return true;
            }
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

            var sqLiteCommand = new SQLiteCommand(
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
                var sqLiteCommand = new SQLiteCommand(
                    // TODO: We should probably occasionally/background clean up items that are expired
                    "SELECT COUNT(*) FROM entries WHERE exp > @exp",
                    this.conn);
                sqLiteCommand.Parameters.AddWithValue("@exp", DateTimeOffset.UtcNow.Ticks);
                int count = (int) sqLiteCommand
                    .ExecuteScalar();
                return count;
            }
        }
    }
}