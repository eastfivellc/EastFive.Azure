using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

using EastFive.Azure.Persistence.StorageTables.Binding;
using EastFive.Reflection;
using EastFive.Serialization.Binding;

using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Persistence.Azure.StorageTables
{
    /// <summary>
    /// Marks a member of an entity as a column to be serialized through the
    /// unified <see cref="EastFive.Serialization.Binding"/> pipeline by
    /// <see cref="StorageTable2Attribute"/>.
    /// <para>
    /// Implements <see cref="IPersistInAzureStorageTables"/> so legacy query
    /// machinery (<see cref="StorageQueryAttribute"/>,
    /// <see cref="TableQueryExtensions.GetTablePropertyName"/>) can resolve the
    /// column name and serialize WHERE-clause values without going through the
    /// old <c>[Storage]</c> attribute. All conversion logic flows through the
    /// binders — this attribute carries no per-shape special-cases.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class ColumnAttribute : Attribute, IPersistInAzureStorageTables
    {
        /// <summary>
        /// Overrides the column name written to / read from the Azure Table.
        /// Defaults to the member's name when null or whitespace.
        /// </summary>
        public string Name { get; set; }

        public ColumnAttribute() { }

        public ColumnAttribute(string name)
        {
            this.Name = name;
        }

        public string GetTablePropertyName(MemberInfo member) =>
            string.IsNullOrWhiteSpace(Name) ? member.Name : Name;

        private static Type MemberType(MemberInfo member) =>
            member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;

        public object GetMemberValue(MemberInfo memberInfo,
            IDictionary<string, EntityProperty> values,
            out bool shouldSkip,
            Func<object> getDefaultValue = default)
        {
            var memberType = MemberType(memberInfo);
            var column = GetTablePropertyName(memberInfo);
            var ctx = new BindingContext(TypeBindings.Default);
            var source = new EntityPropertyRowBindingSource(values);

            object resolved = null;
            var skip = false;
            source.GetScoped<bool>(column,
                child => TypeBindings.Default.Bind<bool>(memberType, child, ctx,
                    v => { resolved = v; return true; },
                    f =>
                    {
                        Trace.TraceWarning(
                            $"[Column] {memberInfo.DeclaringType?.Name}.{memberInfo.Name} " +
                            $"(column '{column}') failed to bind: {f.Reason.Describe()}. Defaulting.");
                        resolved = getDefaultValue != null
                            ? getDefaultValue()
                            : memberType.GetDefault();
                        return true;
                    }).GetAwaiter().GetResult(),
                f =>
                {
                    // Column missing — skip so the legacy walker leaves the
                    // slot at its CLR default. (Graceful schema migration.)
                    skip = true;
                    return true;
                }).GetAwaiter().GetResult();

            shouldSkip = skip;
            return resolved;
        }

        public KeyValuePair<string, EntityProperty>[] ConvertValue<TEntity>(MemberInfo memberInfo,
            object value, IWrapTableEntity<TEntity> tableEntityWrapper)
        {
            var memberType = MemberType(memberInfo);
            var column = GetTablePropertyName(memberInfo);
            var sink = new EntityPropertyRowBindingSink();
            var ctx = new BindingContext(TypeBindings.Default);
            TypeBindings.Default.Emit(memberType, value, sink.Scope(column), ctx);
            return sink.Row.ToArray();
        }
    }
}

