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
    /// Green-field replacement for <see cref="StorageTableAttribute"/> that drives
    /// per-column serialization through the unified <see cref="ITypeBindings"/>
    /// pipeline (<see cref="EntityPropertyRowBindingSource"/> on read,
    /// <see cref="EntityPropertyRowBindingSink"/> on write).
    /// <para>
    /// Key extraction, modifier execution, and the rest of
    /// <see cref="StorageTableAttribute"/>'s machinery are inherited unchanged.
    /// Only the per-column read/write loop is overridden.
    /// </para>
    /// <para>
    /// Members participating in serialization must be marked
    /// <see cref="ColumnAttribute"/>. Parse failures on read are <b>lenient by
    /// default</b> — the failure is logged via <see cref="Trace"/> and the slot
    /// is left at its CLR default, mirroring the legacy "graceful migration"
    /// behavior for dirty rows.
    /// </para>
    /// </summary>
    public class StorageTable2Attribute : StorageTableAttribute
    {
        protected override StorageTableAttribute.TableEntity<TEntity> CreateWrapper<TEntity>() =>
            new TableEntity2<TEntity>();

        private sealed class TableEntity2<EntityType> : StorageTableAttribute.TableEntity<EntityType>
        {
            private static readonly (MemberInfo member, Type type, string column)[] columns =
                BuildColumnPlan();

            private static (MemberInfo, Type, string)[] BuildColumnPlan()
            {
                return typeof(EntityType)
                    .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m is FieldInfo || m is PropertyInfo)
                    .Select(m => (member: m, col: (ColumnAttribute)m.GetCustomAttributes(typeof(ColumnAttribute), inherit: true).FirstOrDefault()))
                    .Where(x => x.col != null)
                    .Select(x =>
                    {
                        var type = x.member is PropertyInfo pi
                            ? pi.PropertyType
                            : ((FieldInfo)x.member).FieldType;
                        var name = string.IsNullOrWhiteSpace(x.col.Name) ? x.member.Name : x.col.Name;
                        return (x.member, type, name);
                    })
                    .ToArray();
            }

            public override void ReadEntity(IDictionary<string, EntityProperty> properties, OperationContext operationContext)
            {
                if (this.Entity == null)
                    this.Entity = Activator.CreateInstance<EntityType>();

                var entity = this.Entity;
                var source = new EntityPropertyRowBindingSource(properties);
                var ctx = new BindingContext(TypeBindings.Default);

                foreach (var (member, type, column) in columns)
                {
                    object resolved = null;
                    var matched = source.GetScoped<bool>(column,
                        child => TypeBindings.Default.Bind<bool>(type, child, ctx,
                            v => { resolved = v; return true; },
                            f =>
                            {
                                Trace.TraceWarning(
                                    $"[StorageTable2] {typeof(EntityType).Name}.{member.Name} (column '{column}') " +
                                    $"failed to bind: {f.Reason.Describe()}. Defaulting.");
                                resolved = type.GetDefault();
                                return true;
                            }).GetAwaiter().GetResult(),
                        f =>
                        {
                            // Column missing — leave slot at default (graceful schema migration).
                            resolved = type.GetDefault();
                            return true;
                        }).GetAwaiter().GetResult();

                    member.SetValue(ref entity, resolved);
                }

                this.Entity = entity;
            }

            public override IDictionary<string, EntityProperty> WriteEntity(OperationContext operationContext)
            {
                var sink = new EntityPropertyRowBindingSink();
                var ctx = new BindingContext(TypeBindings.Default);

                foreach (var (member, type, column) in columns)
                {
                    var value = member is PropertyInfo pi
                        ? pi.GetValue(this.Entity)
                        : ((FieldInfo)member).GetValue(this.Entity);
                    TypeBindings.Default.Emit(type, value, sink.Scope(column), ctx);
                }

                return sink.Row;
            }
        }
    }
}
