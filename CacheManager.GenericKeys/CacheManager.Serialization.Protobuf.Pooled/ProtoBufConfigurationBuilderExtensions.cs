namespace CacheManager.Serialization.Protobuf.Pooled
{
    using CacheManager.Core;
    using CacheManager.Core.Utility;

    using Microsoft.IO;

    /// <summary>
    /// Configuration builder extensions for the <c>ProtoBuf</c> based <see cref="T:CacheManager.Core.Internal.ICacheSerializer" />.
    /// </summary>
    public static class ProtoBufConfigurationBuilderExtensions
    {
        /// <summary>
        /// Configures the cache manager to use the <code>ProtoBuf</code> based cache serializer.
        /// </summary>
        /// <param name="part">The configuration part.</param>
        /// <returns>The builder instance.</returns>
        public static ConfigurationBuilderCachePart WithProtoBufSerializer(
            this ConfigurationBuilderCachePart part,
            RecyclableMemoryStreamManager recyclableMemoryStreamManager)
        {
            Guard.NotNull<ConfigurationBuilderCachePart>(part, nameof(part));
            return part.WithSerializer(typeof(ProtoBufSerializer), recyclableMemoryStreamManager);
        }
    }
}
