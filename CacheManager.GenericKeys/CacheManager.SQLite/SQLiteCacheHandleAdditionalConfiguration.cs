
namespace CacheManager.SQLite
{
    using System;
    using System.Data.SQLite;

    public class SQLiteCacheHandleAdditionalConfiguration
    {
        public delegate SQLiteTransaction BeginTransaction();

        public string DatabaseFilePath { get; set; }

        internal BeginTransaction BeginTransactionMethod { private get; set; }

        public BeginTransaction GetBeginTransactionMethod()
        {
            BeginTransaction beginTransactionMethod = this.BeginTransactionMethod;
            if (beginTransactionMethod == null)
            {
                throw new InvalidOperationException("The configuration has not yet been used to create a CacheHandle, did you call .Build() yet?");
            }

            return beginTransactionMethod;
        }
    }
}