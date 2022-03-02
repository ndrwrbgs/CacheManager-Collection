/// <summary>
/// NOTE: Copied from CacheManager 1.2.0 and modified to change string to TCacheKey
/// </summary>

namespace CacheManager.GenericKeys
{
    using System;
    using System.Text;

    using CacheManager.Core;
    using CacheManager.Core.Internal;
    using JetBrains.Annotations;

    /*
     * DESIGN CHOICE
     *
     * Using CacheItem<(TCacheKey key, TCacheValue value)> to avoid having to make a new type. This makes cacheItem.Key a useless field
     * directly (it uses .Value.key) but avoids copy pastiing a lot of code.
     *
     */

    /// <summary>
    /// This interface is the base contract for the main stack of this library.
    /// <para>
    /// The <c>ICacheHandle</c> and <c>ICacheManager</c> interfaces are derived from <c>ICache</c>,
    /// meaning the method call signature throughout the stack is very similar.
    /// </para>
    /// <para>
    /// We want the flexibility of having a simple get/put/delete cache up to multiple caches
    /// layered on top of each other, still using the same simple and easy to understand interface.
    /// </para>
    /// <para>
    /// The <c>TCacheValue</c> can, but most not be used in the sense of strongly typing. This
    /// means, you can define and configure a cache for certain object types within your domain. But
    /// you can also use <c>object</c> and store anything you want within the cache. All underlying
    /// cache technologies usually do not care about types of the cache items.
    /// </para>
    /// </summary>
    /// <typeparam name="TCacheKey">The type of the cache value.</typeparam>
    /// <typeparam name="TCacheValue">The type of the cache value.</typeparam>
    [PublicAPI]
    public sealed class GenericCache<TCacheKey, TCacheValue> : IDisposable
    {
        private readonly ICacheManager<TCacheValue> cache;
        private readonly ICacheSerializer serializer;

        public GenericCache([NotNull] ICacheManager<TCacheValue> cache)
        {
            this.cache = cache;

            this.serializer = (ICacheSerializer) Activator.CreateInstance(cache.Configuration.SerializerType, cache.Configuration.SerializerTypeArguments);
        }

        /// <summary>
        /// Gets or sets a value for the specified key. The indexer is identical to the
        /// corresponding <see cref="M:CacheManager.Core.ICache`1.Put(TCacheKey,`0)" /> and <see cref="M:CacheManager.Core.ICache`1.Get(TCacheKey)" /> calls.
        /// </summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <returns>The value being stored in the cache for the given <paramref name="key" />.</returns>
        /// <exception cref="T:System.ArgumentNullException">If the <paramref name="key" /> is null.</exception>
        public TCacheValue this[TCacheKey key]
        {
            get => this.cache[this.KeyToString(key)];
            set => this.cache[this.KeyToString(key)] = value;
        }

#if DEBUG
        private TCacheKey keyToString_lastInput;
        private string keyToString_lastOutput;
#endif

        [NotNull]
        private string KeyToString(TCacheKey key)
        {
#if DEBUG
            // TODO: 10% program time is spent allocating strings for this, can we cache recently used items by Reference?
            // This implementation likely won't help other usages, but in this test program where we write-read it can. Remove for production
            if (ReferenceEquals(key, keyToString_lastInput))
            {
                return keyToString_lastOutput;
            }
#endif

            byte[] bytes = this.serializer.Serialize(key);
            string output = Convert.ToBase64String(bytes);

#if DEBUG
            keyToString_lastInput = key;
            keyToString_lastOutput = output;
#endif

            return output;
        }

        private TCacheKey StringToKey([NotNull] string str)
        {
            byte[] bytes = Convert.FromBase64String(str);
            return (TCacheKey) this.serializer.Deserialize(bytes, typeof(TCacheKey));
        }

        /// <summary>
        /// Gets or sets a value for the specified key and region. The indexer is identical to the
        /// corresponding <see cref="M:CacheManager.Core.ICache`1.Put(TCacheKey,`0,System.String)" /> and
        /// <see cref="M:CacheManager.Core.ICache`1.Get(TCacheKey,System.String)" /> calls.
        /// <para>
        /// With <paramref name="region" /> specified, the key will <b>not</b> be found in the global cache.
        /// </para>
        /// </summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <param name="region">The cache region.</param>
        /// <returns>
        /// The value being stored in the cache for the given <paramref name="key" /> and <paramref name="region" />.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        /// If the <paramref name="key" /> or <paramref name="region" /> is null.
        /// </exception>
        public TCacheValue this[TCacheKey key, string region]
        {
            get => this.cache[this.KeyToString(key), region];
            set => this.cache[this.KeyToString(key), region] = value;
        }

        /// <summary>
        /// Adds a value for the specified key to the cache.
        /// <para>
        /// The <c>Add</c> method will <b>not</b> be successful if the specified
        /// <paramref name="key" /> already exists within the cache!
        /// </para>
        /// </summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <param name="value">The value which should be cached.</param>
        /// <returns>
        /// <c>true</c> if the key was not already added to the cache, <c>false</c> otherwise.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        /// If the <paramref name="key" /> or <paramref name="value" /> is null.
        /// </exception>
        public bool Add(TCacheKey key, TCacheValue value) => this.cache.Add(this.KeyToString(key), value);

        /// <summary>
        /// Adds a value for the specified key and region to the cache.
        /// <para>
        /// The <c>Add</c> method will <b>not</b> be successful if the specified
        /// <paramref name="key" /> already exists within the cache!
        /// </para>
        /// <para>
        /// With <paramref name="region" /> specified, the key will <b>not</b> be found in the global cache.
        /// </para>
        /// </summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <param name="value">The value which should be cached.</param>
        /// <param name="region">The cache region.</param>
        /// <returns>
        /// <c>true</c> if the key was not already added to the cache, <c>false</c> otherwise.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        /// If the <paramref name="key" />, <paramref name="value" /> or <paramref name="region" /> is null.
        /// </exception>
        public bool Add(TCacheKey key, TCacheValue value, string region) => this.cache.Add(this.KeyToString(key), value, region);

        /// <summary>
        /// Adds the specified <c>CacheItem</c> to the cache.
        /// <para>
        /// Use this overload to overrule the configured expiration settings of the cache and to
        /// define a custom expiration for this <paramref name="item" /> only.
        /// </para>
        /// <para>
        /// The <c>Add</c> method will <b>not</b> be successful if the specified
        /// <paramref name="item" /> already exists within the cache!
        /// </para>
        /// </summary>
        /// <param name="item">The <c>CacheItem</c> to be added to the cache.</param>
        /// <returns>
        /// <c>true</c> if the key was not already added to the cache, <c>false</c> otherwise.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        /// If the <paramref name="item" /> or the item's key or value is null.
        /// </exception>
        public bool Add(CacheItem<(TCacheKey key, TCacheValue value)> item)
        {
            CacheItem<TCacheValue> newItem = this.ConvertCacheItem(item);
            return this.cache.Add(newItem);
        }

        [NotNull]
        private CacheItem<TCacheValue> ConvertCacheItem([NotNull] CacheItem<(TCacheKey key, TCacheValue value)> item)
        {
            return item.Region != null
                ? new CacheItem<TCacheValue>(
                        this.KeyToString(item.Value.key),
                        item.Region,
                        item.Value.value,
                        item.ExpirationMode,
                        item.ExpirationTimeout)
                    {LastAccessedUtc = item.LastAccessedUtc}
                : new CacheItem<TCacheValue>(
                        this.KeyToString(item.Value.key),
                        item.Value.value,
                        item.ExpirationMode,
                        item.ExpirationTimeout)
                    {LastAccessedUtc = item.LastAccessedUtc};
        }

        [CanBeNull]
        private CacheItem<(TCacheKey key, TCacheValue value)> ConvertCacheItem(
            [CanBeNull] CacheItem<TCacheValue> item,
            // Performance optimization in the case item was looked up by key, so we don't need to serialize again
            TCacheKey keyIfAlreadyKnown = default)
        {
            if (item == null)
            {
                return null;
            }

            TCacheKey genericKey = ReferenceEquals(keyIfAlreadyKnown, default)
                ? this.StringToKey(item.Key)
                : keyIfAlreadyKnown;

            return item.Region != null
                ? new CacheItem<(TCacheKey key, TCacheValue value)>(
                        "unused",
                        item.Region,
                        (key: genericKey, value: item.Value),
                        item.ExpirationMode,
                        item.ExpirationTimeout)
                    {LastAccessedUtc = item.LastAccessedUtc}
                : new CacheItem<(TCacheKey key, TCacheValue value)>(
                        "unused",
                        (key: genericKey, value: item.Value),
                        item.ExpirationMode,
                        item.ExpirationTimeout)
                    {LastAccessedUtc = item.LastAccessedUtc};
        }

        /// <summary>
        /// Clears this cache, removing all items in the base cache and all regions.
        /// </summary>
        public void Clear() => this.cache.Clear();

        /// <summary>
        /// Clears the cache region, removing all items from the specified <paramref name="region" /> only.
        /// </summary>
        /// <param name="region">The cache region.</param>
        /// <exception cref="T:System.ArgumentNullException">If the <paramref name="region" /> is null.</exception>
        public void ClearRegion(string region) => this.cache.ClearRegion(region);

        /// <summary>
        /// Returns a value indicating if the <paramref name="key" /> exists in at least one cache layer
        /// configured in CacheManger, without actually retrieving it from the cache.
        /// </summary>
        /// <param name="key">The cache key to check.</param>
        /// <returns><c>True</c> if the <paramref name="key" /> exists, <c>False</c> otherwise.</returns>
        public bool Exists(TCacheKey key) => this.cache.Exists(this.KeyToString(key));

        /// <summary>
        /// Returns a value indicating if the <paramref name="key" /> in <paramref name="region" /> exists in at least one cache layer
        /// configured in CacheManger, without actually retrieving it from the cache (if supported).
        /// </summary>
        /// <param name="key">The cache key to check.</param>
        /// <param name="region">The cache region.</param>
        /// <returns><c>True</c> if the <paramref name="key" /> exists, <c>False</c> otherwise.</returns>
        public bool Exists(TCacheKey key, string region) => this.cache.Exists(this.KeyToString(key), region);

        /// <summary>Gets a value for the specified key.</summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <returns>The value being stored in the cache for the given <paramref name="key" />.</returns>
        /// <exception cref="T:System.ArgumentNullException">If the <paramref name="key" /> is null.</exception>
        public TCacheValue Get(TCacheKey key) => this.cache.Get(this.KeyToString(key));

        /// <summary>Gets a value for the specified key and region.</summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <param name="region">The cache region.</param>
        /// <returns>
        /// The value being stored in the cache for the given <paramref name="key" /> and <paramref name="region" />.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        /// If the <paramref name="key" /> or <paramref name="region" /> is null.
        /// </exception>
        public TCacheValue Get(TCacheKey key, string region) => this.cache.Get(this.KeyToString(key), region);

        /// <summary>
        /// Gets a value for the specified key and will cast it to the specified type.
        /// </summary>
        /// <typeparam name="TOut">The type the value is converted and returned.</typeparam>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <returns>The value being stored in the cache for the given <paramref name="key" />.</returns>
        /// <exception cref="T:System.ArgumentNullException">If the <paramref name="key" /> is null.</exception>
        /// <exception cref="T:System.InvalidCastException">
        /// If no explicit cast is defined from <c>TCacheValue</c> to <c>TOut</c>.
        /// </exception>
        public TOut Get<TOut>(TCacheKey key) => this.cache.Get<TOut>(this.KeyToString(key));

        /// <summary>
        /// Gets a value for the specified key and region and will cast it to the specified type.
        /// </summary>
        /// <typeparam name="TOut">The type the cached value should be converted to.</typeparam>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <param name="region">The cache region.</param>
        /// <returns>
        /// The value being stored in the cache for the given <paramref name="key" /> and <paramref name="region" />.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        /// If the <paramref name="key" /> or <paramref name="region" /> is null.
        /// </exception>
        /// <exception cref="T:System.InvalidCastException">
        /// If no explicit cast is defined from <c>TCacheValue</c> to <c>TOut</c>.
        /// </exception>
        public TOut Get<TOut>(TCacheKey key, string region) => this.cache.Get<TOut>(this.KeyToString(key), region);

        /// <summary>
        /// Gets the <c>CacheItem</c> for the specified key.
        /// </summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <returns>The <c>CacheItem</c>.</returns>
        /// <exception cref="T:System.ArgumentNullException">If the <paramref name="key" /> is null.</exception>
        [CanBeNull]
        public CacheItem<(TCacheKey key, TCacheValue value)> GetCacheItem(TCacheKey key)
        {
            string keyToString = this.KeyToString(key);
            CacheItem<TCacheValue> cacheItem = this.cache.GetCacheItem(keyToString);
            CacheItem<(TCacheKey key, TCacheValue value)> convertCacheItem = this.ConvertCacheItem(cacheItem, key);
            return convertCacheItem;
        }

        /// <summary>
        /// Gets the <c>CacheItem</c> for the specified key and region.
        /// </summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <param name="region">The cache region.</param>
        /// <returns>The <c>CacheItem</c>.</returns>
        /// <exception cref="T:System.ArgumentNullException">
        /// If the <paramref name="key" /> or <paramref name="region" /> is null.
        /// </exception>
        [CanBeNull]
        public CacheItem<(TCacheKey key, TCacheValue value)> GetCacheItem(TCacheKey key, string region) =>
            this.ConvertCacheItem(this.cache.GetCacheItem(this.KeyToString(key), region));

        /// <summary>
        /// Puts a value for the specified key into the cache.
        /// <para>
        /// If the <paramref name="key" /> already exists within the cache, the existing value will
        /// be replaced with the new <paramref name="value" />.
        /// </para>
        /// </summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <param name="value">The value which should be cached.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// If the <paramref name="key" /> or <paramref name="value" /> is null.
        /// </exception>
        public void Put(TCacheKey key, TCacheValue value) => this.cache.Put(this.KeyToString(key), value);

        /// <summary>
        /// Puts a value for the specified key and region into the cache.
        /// <para>
        /// If the <paramref name="key" /> already exists within the cache, the existing value will
        /// be replaced with the new <paramref name="value" />.
        /// </para>
        /// <para>
        /// With <paramref name="region" /> specified, the key will <b>not</b> be found in the global cache.
        /// </para>
        /// </summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <param name="value">The value which should be cached.</param>
        /// <param name="region">The cache region.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// If the <paramref name="key" />, <paramref name="value" /> or <paramref name="region" /> is null.
        /// </exception>
        public void Put(TCacheKey key, TCacheValue value, string region) => this.cache.Put(this.KeyToString(key), value, region);

        /// <summary>
        /// Puts the specified <c>CacheItem</c> into the cache.
        /// <para>
        /// If the <paramref name="item" /> already exists within the cache, the existing item will
        /// be replaced with the new <paramref name="item" />.
        /// </para>
        /// <para>
        /// Use this overload to overrule the configured expiration settings of the cache and to
        /// define a custom expiration for this <paramref name="item" /> only.
        /// </para>
        /// </summary>
        /// <param name="item">The <c>CacheItem</c> to be cached.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// If the <paramref name="item" /> or the item's key or value is null.
        /// </exception>
        public void Put([NotNull] CacheItem<(TCacheKey key, TCacheValue value)> item) => this.cache.Put(this.ConvertCacheItem(item));

        /// <summary>Removes a value from the cache for the specified key.</summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <returns>
        /// <c>true</c> if the key was found and removed from the cache, <c>false</c> otherwise.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">If the <paramref name="key" /> is null.</exception>
        public bool Remove(TCacheKey key) => this.cache.Remove(this.KeyToString(key));

        /// <summary>
        /// Removes a value from the cache for the specified key and region.
        /// </summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <param name="region">The cache region.</param>
        /// <returns>
        /// <c>true</c> if the key was found and removed from the cache, <c>false</c> otherwise.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        /// If the <paramref name="key" /> or <paramref name="region" /> is null.
        /// </exception>
        public bool Remove(TCacheKey key, string region) => this.cache.Remove(this.KeyToString(key), region);

        public void Dispose()
        {
            this.cache.Dispose();
        }
    }
}