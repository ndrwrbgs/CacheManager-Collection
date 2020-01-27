namespace CacheManager.Serialization.Protobuf.Pooled {
    using System;
    using System.IO;

    using CacheManager.Core.Internal;

    using Microsoft.IO;

    using ProtoBuf;

    /// <summary>
    /// Implements the <see cref="T:CacheManager.Core.Internal.ICacheSerializer" /> contract using <c>ProtoBuf</c>.
    /// </summary>
    public class ProtoBufSerializer : CacheSerializer
    {
        private static readonly Type _openGenericItemType = typeof(ProtoBufCacheItem<>);

        private readonly RecyclableMemoryStreamManager recyclableMemoryStreamManager;
        public ProtoBufSerializer(RecyclableMemoryStreamManager recyclableMemoryStreamManager)
        {
            this.recyclableMemoryStreamManager = recyclableMemoryStreamManager;
        }

        /// <inheritdoc />
        public override object Deserialize(byte[] data, Type target)
        {
            int index = 0;
            if (data.Length != 0)
                index = 1;
            using (MemoryStream memoryStream = new MemoryStream(data, index, data.Length - index))
            {
                return Serializer.Deserialize(target, (Stream)memoryStream);
            }
        }

        /// <inheritdoc />
        public override byte[] Serialize<T>(T value)
        {
            using (var memoryStream = this.recyclableMemoryStreamManager.GetStream())
            {
                memoryStream.WriteByte((byte)0);
                Serializer.Serialize<T>((Stream)memoryStream, value);
                return memoryStream.ToArray();
            }
        }

        /// <inheritdoc />
        protected override object CreateNewItem<TCacheValue>(
            ICacheItemProperties properties,
            object value)
        {
            return (object)new ProtoBufCacheItem<TCacheValue>(properties, value);
        }

        /// <inheritdoc />
        protected override Type GetOpenGeneric()
        {
            return ProtoBufSerializer._openGenericItemType;
        }
    }
}