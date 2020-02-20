
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
        }

        private static SQLiteConnection CreateConnection(string databaseFilePath)
        {
            if (!File.Exists(databaseFilePath))
            {
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
                // TODO: Params for injection
                $"SELECT COUNT(*) FROM entries WHERE key = @key", // TODO: Filter out expired
                this.conn);
            sqLiteCommand.Parameters.AddWithValue("@key", key);
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
                // TODO: Delete the value that is expired?
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
                $"DELETE FROM entries WHERE key = @key",
                this.conn);
            sqLiteCommand.Parameters.AddWithValue("@key", key);
            int rowsAffected = sqLiteCommand
                .ExecuteNonQuery();

            return rowsAffected != 0;
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
                    // TODO: Implement expiry
                    $"INSERT INTO entries (key, val, exp)"
                    + $" VALUES (@key, @val, @exp)",
                    this.conn);
                sqLiteCommand.Parameters.AddWithValue("@key", item.Key);
                sqLiteCommand.Parameters.AddWithValue("@val", serializedValueBytes);
                sqLiteCommand.Parameters.AddWithValue("@exp", DateTimeOffset.Parse("1/1/2900").ToUniversalTime().Ticks);
                sqLiteCommand
                    .ExecuteNonQuery();

                tr.Commit();
                return true;
            }
        }

        protected override void PutInternalPrepared(CacheItem<TCacheValue> item)
        {
            var serializedValueBytes = this.serializer.Serialize(item.Value);

            var sqLiteCommand = new SQLiteCommand(
                // TODO: Implement expiry
                $"REPLACE INTO entries (key, val, exp)"
                + $" VALUES (@key, @val, @exp)",
                this.conn);
            sqLiteCommand.Parameters.AddWithValue("@key", item.Key);
            sqLiteCommand.Parameters.AddWithValue("@val", serializedValueBytes);
            sqLiteCommand.Parameters.AddWithValue("@exp", DateTimeOffset.Parse("1/1/2900").ToUniversalTime().Ticks);
            sqLiteCommand
                .ExecuteNonQuery();
        }

        public override int Count
        {
            get
            {
                int count = (int) new SQLiteCommand(
                        "SELECT COUNT(*) FROM entries",
                        this.conn)
                    .ExecuteScalar();
                return count;
            }
        }
    }
}