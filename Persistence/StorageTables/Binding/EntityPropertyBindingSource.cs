using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Serialization.Binding;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Persistence.StorageTables.Binding
{
    /// <summary>
    /// <see cref="IBindingSource"/> over a single Azure Table <see cref="EntityProperty"/>.
    /// Scalar only — composite row scoping (member-by-member access against a row of
    /// <c>IDictionary&lt;string,EntityProperty&gt;</c>) is the responsibility of a future
    /// <c>EntityPropertyRowBindingSource</c>.
    ///
    /// <para>Null is recognized when:
    /// <list type="bullet">
    ///   <item>The <see cref="EntityProperty"/> reference itself is <c>null</c>.</item>
    ///   <item>The strongly-typed value is absent (e.g., <c>BooleanValue.HasValue == false</c>).</item>
    ///   <item>The value is a Guid equal to <see cref="EDMExtensions.NullGuidKey"/> (the
    ///     historical EDM sentinel for stored nulls).</item>
    ///   <item>The value is a binary array that is <c>null</c> or empty.</item>
    /// </list>
    /// Cross-EdmType coercion (e.g., <c>Guid</c> target from <c>EdmType.String</c>) is
    /// preserved from the legacy <c>EntityPropertyExtensions.ParseCoreTypes</c> behavior.
    /// </para>
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

        private static ValueTask<TResult> Null<TResult>(Type expected, Func<BindFailure, TResult> onFailure, Func<TResult> onNull)
        {
            if (onNull is not null)
                return new ValueTask<TResult>(onNull());
            return new ValueTask<TResult>(onFailure(new BindFailure(new NullValue(), expected)));
        }

        private static ValueTask<TResult> Wrong<TResult>(string expected, EdmType got, Type targetType, Func<BindFailure, TResult> onFailure) =>
            new(onFailure(new BindFailure(new WrongSourceType(expected, got.ToString()), targetType)));

        private static ValueTask<TResult> Parse<TResult>(string detail, Type targetType, Func<BindFailure, TResult> onFailure) =>
            new(onFailure(new BindFailure(new ParseError(detail), targetType)));

        public ValueTask<TResult> GetString<TResult>(Func<string, TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
        {
            if (IsNullScalar) return Null(typeof(string), onFailure, onNull);
            if (value.PropertyType == EdmType.String)
                return new ValueTask<TResult>(onValue(value.StringValue));
            return Wrong("string", value.PropertyType, typeof(string), onFailure);
        }

        public ValueTask<TResult> GetGuid<TResult>(Func<Guid, TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
        {
            if (IsNullScalar) return Null(typeof(Guid), onFailure, onNull);
            switch (value.PropertyType)
            {
                case EdmType.Guid:
                    return new ValueTask<TResult>(onValue(value.GuidValue.Value));
                case EdmType.String:
                    if (Guid.TryParse(value.StringValue, out var g))
                        return new ValueTask<TResult>(onValue(g));
                    return Parse($"'{value.StringValue}' is not a Guid", typeof(Guid), onFailure);
                default:
                    return Wrong("guid", value.PropertyType, typeof(Guid), onFailure);
            }
        }

        public ValueTask<TResult> GetBool<TResult>(Func<bool, TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
        {
            if (IsNullScalar) return Null(typeof(bool), onFailure, onNull);
            switch (value.PropertyType)
            {
                case EdmType.Boolean:
                    return new ValueTask<TResult>(onValue(value.BooleanValue.Value));
                case EdmType.String:
                    if (bool.TryParse(value.StringValue, out var b))
                        return new ValueTask<TResult>(onValue(b));
                    return Parse($"'{value.StringValue}' is not a Boolean", typeof(bool), onFailure);
                default:
                    return Wrong("bool", value.PropertyType, typeof(bool), onFailure);
            }
        }

        public ValueTask<TResult> GetInt64<TResult>(Func<long, TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
        {
            if (IsNullScalar) return Null(typeof(long), onFailure, onNull);
            switch (value.PropertyType)
            {
                case EdmType.Int64:
                    return new ValueTask<TResult>(onValue(value.Int64Value.Value));
                case EdmType.Int32:
                    return new ValueTask<TResult>(onValue(value.Int32Value.Value));
                case EdmType.String:
                    if (long.TryParse(value.StringValue, out var v))
                        return new ValueTask<TResult>(onValue(v));
                    return Parse($"'{value.StringValue}' is not an integer", typeof(long), onFailure);
                default:
                    return Wrong("integer", value.PropertyType, typeof(long), onFailure);
            }
        }

        public ValueTask<TResult> GetDouble<TResult>(Func<double, TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
        {
            if (IsNullScalar) return Null(typeof(double), onFailure, onNull);
            switch (value.PropertyType)
            {
                case EdmType.Double:
                    return new ValueTask<TResult>(onValue(value.DoubleValue.Value));
                case EdmType.Int32:
                    return new ValueTask<TResult>(onValue(value.Int32Value.Value));
                case EdmType.Int64:
                    return new ValueTask<TResult>(onValue(value.Int64Value.Value));
                case EdmType.String:
                    if (double.TryParse(value.StringValue, out var v))
                        return new ValueTask<TResult>(onValue(v));
                    return Parse($"'{value.StringValue}' is not a number", typeof(double), onFailure);
                default:
                    return Wrong("number", value.PropertyType, typeof(double), onFailure);
            }
        }

        public ValueTask<TResult> GetDateTime<TResult>(Func<DateTime, TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
        {
            if (IsNullScalar) return Null(typeof(DateTime), onFailure, onNull);
            switch (value.PropertyType)
            {
                case EdmType.DateTime:
                    return new ValueTask<TResult>(onValue(value.DateTime.Value));
                case EdmType.Int64:
                    // Legacy: ticks-encoded DateTime fallback.
                    return new ValueTask<TResult>(onValue(new DateTime(value.Int64Value.Value)));
                case EdmType.String:
                    if (DateTime.TryParse(value.StringValue, out var dt))
                        return new ValueTask<TResult>(onValue(dt));
                    return Parse($"'{value.StringValue}' is not a DateTime", typeof(DateTime), onFailure);
                default:
                    return Wrong("datetime", value.PropertyType, typeof(DateTime), onFailure);
            }
        }

        public ValueTask<TResult> GetBytes<TResult>(Func<byte[], TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
        {
            if (IsNullScalar) return Null(typeof(byte[]), onFailure, onNull);
            if (value.PropertyType == EdmType.Binary)
                return new ValueTask<TResult>(onValue(value.BinaryValue));
            return Wrong("binary", value.PropertyType, typeof(byte[]), onFailure);
        }

        public ValueTask<TResult> GetScoped<TResult>(string key, Func<IBindingSource, TResult> onChild, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
        {
            if (IsNullScalar) return Null(typeof(object), onFailure, onNull);
            return new ValueTask<TResult>(onFailure(new BindFailure(
                new WrongSourceType("object", value.PropertyType.ToString()), typeof(object))));
        }

        public ValueTask<TResult> GetIndexed<TResult>(int index, Func<IBindingSource, TResult> onChild, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
        {
            if (IsNullScalar) return Null(typeof(object), onFailure, onNull);
            return new ValueTask<TResult>(onFailure(new BindFailure(
                new WrongSourceType("array", value.PropertyType.ToString()), typeof(object))));
        }

        public ValueTask<TResult> GetArray<TResult>(Func<IEnumerable<IBindingSource>, TResult> onItems, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
        {
            if (IsNullScalar) return Null(typeof(object), onFailure, onNull);
            return new ValueTask<TResult>(onFailure(new BindFailure(
                new WrongSourceType("array", value.PropertyType.ToString()), typeof(object))));
        }

        public ValueTask<TResult> GetMembers<TResult>(Func<IEnumerable<KeyValuePair<string, IBindingSource>>, TResult> onMembers, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
        {
            if (IsNullScalar) return Null(typeof(object), onFailure, onNull);
            return new ValueTask<TResult>(onFailure(new BindFailure(
                new WrongSourceType("object", value.PropertyType.ToString()), typeof(object))));
        }
    }
}
