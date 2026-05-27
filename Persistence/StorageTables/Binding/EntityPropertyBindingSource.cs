using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Serialization;
using EastFive.Serialization.Binding;
using EastFive.Serialization.Binding.Sources;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Persistence.StorageTables.Binding
{
    /// <summary>
    /// <see cref="IBindingSource"/> over a single Azure Table <see cref="EntityProperty"/>.
    /// Dispatches on <see cref="EdmType"/> — no source-side coercion. A
    /// <see cref="EdmType.Binary"/> cell calls <c>onBytes</c>, or <c>onArray</c> when
    /// the caller provides an <c>elementTypeHint</c> matching the packed primitive
    /// element (legacy <c>ByteArrayExtensions</c> convention).
    /// </summary>
    public sealed class EntityPropertyBindingSource : IBindingSource
    {
        private static readonly Guid NullGuidSentinel = new Guid(EDMExtensions.NullGuidKey);

        private readonly EntityProperty value;

        public EntityPropertyBindingSource(EntityProperty value)
        {
            this.value = value;
        }

        private bool IsNullScalar =>
            value is null ||
            (value.PropertyType == EdmType.Boolean && !value.BooleanValue.HasValue) ||
            (value.PropertyType == EdmType.DateTime && !value.DateTime.HasValue) ||
            (value.PropertyType == EdmType.Double && !value.DoubleValue.HasValue) ||
            (value.PropertyType == EdmType.Int32 && !value.Int32Value.HasValue) ||
            (value.PropertyType == EdmType.Int64 && !value.Int64Value.HasValue) ||
            (value.PropertyType == EdmType.Guid && (!value.GuidValue.HasValue || value.GuidValue.Value == NullGuidSentinel)) ||
            (value.PropertyType == EdmType.Binary && (value.BinaryValue is null || value.BinaryValue.Length == 0)) ||
            (value.PropertyType == EdmType.String && value.StringValue is null);

        public ValueTask<TResult> GetValue<TResult>(
            string path = null,
            Func<TResult> onNull = null,
            Func<string, TResult> onString = null,
            Func<Guid, TResult> onGuid = null,
            Func<bool, TResult> onBool = null,
            Func<long, TResult> onInt64 = null,
            Func<double, TResult> onDouble = null,
            Func<DateTime, TResult> onDateTime = null,
            Func<byte[], TResult> onBytes = null,
            Func<IBindingSource, TResult> onObject = null,
            Func<IEnumerableBindingSource, TResult> onArray = null,
            Type elementTypeHint = null,
            Func<BindFailure, TResult> onFailure = null)
        {
            if (!string.IsNullOrEmpty(path))
                return BindingSourceDispatch.WrongType<TResult>(
                    "scalar", "navigation into EDM cell", typeof(object), path, onFailure);

            if (IsNullScalar)
                return BindingSourceDispatch.Null(typeof(object), path, onNull, onFailure);

            var expected = BindingSourceDispatch.InferExpected(
                hasString: onString is not null, hasGuid: onGuid is not null, hasBool: onBool is not null,
                hasInt64: onInt64 is not null, hasDouble: onDouble is not null, hasDateTime: onDateTime is not null,
                hasBytes: onBytes is not null, hasObject: onObject is not null, hasArray: onArray is not null);

            switch (value.PropertyType)
            {
                case EdmType.String:
                    if (onString is not null) return new ValueTask<TResult>(onString(value.StringValue));
                    return BindingSourceDispatch.WrongType<TResult>(expected, "String", typeof(object), path, onFailure);

                case EdmType.Guid:
                    if (onGuid is not null) return new ValueTask<TResult>(onGuid(value.GuidValue.Value));
                    return BindingSourceDispatch.WrongType<TResult>(expected, "Guid", typeof(object), path, onFailure);

                case EdmType.Boolean:
                    if (onBool is not null) return new ValueTask<TResult>(onBool(value.BooleanValue.Value));
                    return BindingSourceDispatch.WrongType<TResult>(expected, "Boolean", typeof(object), path, onFailure);

                case EdmType.Int32:
                    if (onInt64 is not null) return new ValueTask<TResult>(onInt64(value.Int32Value.Value));
                    return BindingSourceDispatch.WrongType<TResult>(expected, "Int32", typeof(object), path, onFailure);

                case EdmType.Int64:
                    if (onInt64 is not null) return new ValueTask<TResult>(onInt64(value.Int64Value.Value));
                    return BindingSourceDispatch.WrongType<TResult>(expected, "Int64", typeof(object), path, onFailure);

                case EdmType.Double:
                    if (onDouble is not null) return new ValueTask<TResult>(onDouble(value.DoubleValue.Value));
                    return BindingSourceDispatch.WrongType<TResult>(expected, "Double", typeof(object), path, onFailure);

                case EdmType.DateTime:
                    if (onDateTime is not null) return new ValueTask<TResult>(onDateTime(value.DateTime.Value));
                    return BindingSourceDispatch.WrongType<TResult>(expected, "DateTime", typeof(object), path, onFailure);

                case EdmType.Binary:
                    if (onArray is not null && elementTypeHint is not null)
                    {
                        var unpacked = TryUnpack(value.BinaryValue, elementTypeHint);
                        if (unpacked is not null)
                            return new ValueTask<TResult>(onArray(new EnumerableBindingSource(unpacked, elementTypeHint)));
                    }
                    if (onBytes is not null) return new ValueTask<TResult>(onBytes(value.BinaryValue));
                    return BindingSourceDispatch.WrongType<TResult>(expected, "Binary", typeof(object), path, onFailure);

                default:
                    return BindingSourceDispatch.WrongType<TResult>(expected, value.PropertyType.ToString(), typeof(object), path, onFailure);
            }
        }

        private static IEnumerable<IBindingSource> TryUnpack(byte[] bytes, Type elementType)
        {
            if (bytes is null) return null;

            if (elementType == typeof(Guid) && bytes.Length % 16 == 0)
            {
                IEnumerable<IBindingSource> Items()
                {
                    for (var i = 0; i < bytes.Length; i += 16)
                    {
                        var chunk = new byte[16];
                        Buffer.BlockCopy(bytes, i, chunk, 0, 16);
                        yield return new EntityPropertyBindingSource(new EntityProperty(new Guid(chunk)));
                    }
                }
                return Items();
            }

            if (elementType == typeof(int))
                return bytes.ToIntsFromByteArray().Select(v => (IBindingSource)new EntityPropertyBindingSource(new EntityProperty(v)));

            if (elementType == typeof(long))
                return bytes.ToLongsFromByteArray().Select(v => (IBindingSource)new EntityPropertyBindingSource(new EntityProperty(v)));

            if (elementType == typeof(double))
                return bytes.ToDoublesFromByteArray().Select(v => (IBindingSource)new EntityPropertyBindingSource(new EntityProperty(v)));

            if (elementType == typeof(DateTime))
                return bytes.ToDateTimesFromByteArray().Select(v => (IBindingSource)new EntityPropertyBindingSource(new EntityProperty(v)));

            if (elementType == typeof(string))
                return bytes.ToStringsFromUTF8ByteArray().Select(v => (IBindingSource)new EntityPropertyBindingSource(new EntityProperty(v)));

            return null;
        }
    }
}
