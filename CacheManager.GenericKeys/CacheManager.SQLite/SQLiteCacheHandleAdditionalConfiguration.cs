
namespace CacheManager.SQLite
{
    using System.Data.SQLite;

    public class SQLiteCacheHandleAdditionalConfiguration
    {
        public delegate SQLiteTransaction BeginTransaction();

        public delegate void SaveBeginTransactionDelegate(BeginTransaction beginTransaction);

        /// <summary>
        /// Set this callback and the <see cref="SQLiteCacheHandle{TCacheValue}"/> will call it
        /// with a reference to the method that can be used to begin the transaction.
        /// </summary>
        /// <remarks>
        /// It might take some work to wrap your head around this, but essentially you're telling
        /// the cache handle 'how to tell you' about how to begin a transaction. This is an artifact
        /// of CacheManager not exposing the CacheHandles directly and an attempt to avoid having to
        /// use unsafe casts.
        /// </remarks>
        public SaveBeginTransactionDelegate SaveBeginTransactionMethod { get; set; }

        public string DatabaseFilePath { get; set; }
    }
}