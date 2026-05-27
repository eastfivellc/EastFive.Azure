using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EastFive.Serialization.Binding;
using EastFive.Serialization.Binding.Sources;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Persistence.StorageTables.Binding
{
    /// <summary>
    /// Composite <see cref="IBindingSource"/> over an Azure Table row — a flat
    /// column-name → <see cref="EntityProperty"/> dictionary. Empty <c>path</c>
    /// reports as an object via <c>onObject</c>; non-empty paths resolve the head
    /// segment to a column and forward the rest to that column's
    /// <see cref="EntityPropertyBindingSource"/>.
    /// </summary>
    public sealed class EntityPropertyRowBindingSource : IBindingSource
    {
        private readonly IDictionary<string, EntityProperty> row;

        public EntityPropertyRowBindingSource(IDictionary<string, EntityProperty> row)
        {
            this.row = row ?? new Dictionary<string, EntityProperty>();
        }

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
            if (string.IsNullOrEmpty(path))
            {
                if (onObject is not null) return new ValueTask<TResult>(onObject(this));
                return BindingSourceDispatch.WrongType<TResult>("object", "row", typeof(object), path, onFailure);
            }

            if (!PathParser.TryConsumeName(path, out var columnName, out var rest))
                return BindingSourceDispatch.FailTask(
                    new BindFailure(new ParseError($"Cannot parse path '{path}'"), typeof(object), path), onFailure);

            if (!TryGetColumn(columnName, out var ep))
                return BindingSourceDispatch.FailTask(
                    new BindFailure(new NotPresent(), typeof(object), path), onFailure);

            var cellSource = new EntityPropertyBindingSource(ep);
            return cellSource.GetValue(rest, onNull, onString, onGuid, onBool, onInt64, onDouble,
                onDateTime, onBytes, onObject, onArray, elementTypeHint, onFailure);
        }

        private bool TryGetColumn(string name, out EntityProperty ep)
        {
            if (row.TryGetValue(name, out ep)) return true;
            foreach (var kv in row)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    ep = kv.Value;
                    return true;
                }
            }
            ep = null;
            return false;
        }
    }
}
