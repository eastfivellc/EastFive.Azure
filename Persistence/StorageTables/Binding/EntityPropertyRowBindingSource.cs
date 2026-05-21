using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EastFive.Serialization.Binding;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Persistence.StorageTables.Binding
{
    /// <summary>
    /// Composite <see cref="IBindingSource"/> over an Azure Table row — a flat
    /// <see cref="IDictionary{TKey, TValue}"/> of column-name → <see cref="EntityProperty"/>.
    /// <para>
    /// Scalar accessors fail with <see cref="WrongSourceType"/> (a row isn't a
    /// scalar). <see cref="GetScoped"/> looks up the column by name and returns
    /// a <see cref="EntityPropertyBindingSource"/> wrapper; a missing column
    /// surfaces as <see cref="NotPresent"/>, distinct from a column whose value
    /// is null.
    /// </para>
    /// </summary>
    public sealed class EntityPropertyRowBindingSource : IBindingSource
    {
        private readonly IDictionary<string, EntityProperty> row;

        public EntityPropertyRowBindingSource(IDictionary<string, EntityProperty> row)
        {
            this.row = row ?? new Dictionary<string, EntityProperty>();
        }

        private static ValueTask<TResult> Wrong<TResult>(string expected, Type target, Func<BindFailure, TResult> onFailure) =>
            new(onFailure(new BindFailure(new WrongSourceType(expected, "row"), target)));

        public ValueTask<TResult> GetString<TResult>(Func<string, TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
            => Wrong("string", typeof(string), onFailure);
        public ValueTask<TResult> GetGuid<TResult>(Func<Guid, TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
            => Wrong("guid", typeof(Guid), onFailure);
        public ValueTask<TResult> GetBool<TResult>(Func<bool, TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
            => Wrong("bool", typeof(bool), onFailure);
        public ValueTask<TResult> GetInt64<TResult>(Func<long, TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
            => Wrong("integer", typeof(long), onFailure);
        public ValueTask<TResult> GetDouble<TResult>(Func<double, TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
            => Wrong("number", typeof(double), onFailure);
        public ValueTask<TResult> GetDateTime<TResult>(Func<DateTime, TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
            => Wrong("datetime", typeof(DateTime), onFailure);
        public ValueTask<TResult> GetBytes<TResult>(Func<byte[], TResult> onValue, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
            => Wrong("binary", typeof(byte[]), onFailure);

        public ValueTask<TResult> GetScoped<TResult>(string key, Func<IBindingSource, TResult> onChild, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
        {
            if (!row.TryGetValue(key, out var ep))
                return new ValueTask<TResult>(onFailure(new BindFailure(new NotPresent(), typeof(object))));
            return new ValueTask<TResult>(onChild(new EntityPropertyBindingSource(ep)));
        }

        public ValueTask<TResult> GetIndexed<TResult>(int index, Func<IBindingSource, TResult> onChild, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
            => Wrong("array", typeof(object), onFailure);

        public ValueTask<TResult> GetArray<TResult>(Func<IEnumerable<IBindingSource>, TResult> onItems, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
            => Wrong("array", typeof(object), onFailure);

        public ValueTask<TResult> GetMembers<TResult>(Func<IEnumerable<KeyValuePair<string, IBindingSource>>, TResult> onMembers, Func<BindFailure, TResult> onFailure, Func<TResult> onNull = null)
        {
            IEnumerable<KeyValuePair<string, IBindingSource>> Project()
            {
                foreach (var kvp in row)
                    yield return new KeyValuePair<string, IBindingSource>(kvp.Key, new EntityPropertyBindingSource(kvp.Value));
            }
            return new ValueTask<TResult>(onMembers(Project()));
        }
    }
}
