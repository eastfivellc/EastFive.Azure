using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive.Api;
using EastFive.Api.Binding;
using EastFive.Api.Bindings;
using EastFive.Api.Serialization.Binding.Sources;
using EastFive.Extensions;
using EastFive.Reflection;
using EastFive.Serialization.Binding;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// Base for the V3 loader attributes
    /// (<c>[StorageEntityFromQueryId]</c>,
    /// <c>[StorageEntityFromQueryParam]</c>,
    /// <c>[StorageEntityFromRoute]</c>) that pre-load a record into a
    /// <see cref="StorageEntity{T}"/> parameter slot.
    /// <para>
    /// <b>Selection</b> (sync, in <see cref="TrySelectSource"/>): the attribute
    /// discovers the entity's key members, resolves each one's wire name, and
    /// reads each raw string from <see cref="IRequestEnvelopeV3.Query"/> and/or
    /// <see cref="IRequestEnvelopeV3.Route"/> per <see cref="Source"/>. If any
    /// key part is missing the method does not apply. On a hit it packs the
    /// (wire-name → string) pairs onto a <see cref="LookupBindingSource"/> and
    /// emits a <see cref="BindCalls.FromSource"/> call. No driver lookup
    /// happens here — the binder resolves the driver lazily via
    /// <see cref="ParameterSlot"/>.
    /// </para>
    /// <para>
    /// <b>Bind</b> (async, in <see cref="StorageEntityBinder"/>): the binder
    /// unwraps the lookup source, parses each key part through its registered
    /// <c>ITypeBinder</c> via <c>ITypeBindings.Bind</c> (so <c>IRef&lt;T&gt;</c>,
    /// <c>Guid</c>, custom keys all go through their own binders), builds the
    /// partial entity, resolves the driver via <see cref="StorageDriverScope"/>
    /// on the <see cref="ParameterSlot"/>, runs the async load, and surfaces
    /// the loaded full <see cref="StorageEntity{T}"/> (or a 404-coded
    /// <see cref="BindFailure"/>).
    /// </para>
    /// <para>
    /// <see cref="IModifyRoutePattern"/> is kept here because route-pattern
    /// shaping is orthogonal to dispatch — it runs at route-registration
    /// time and lets path-sourced loaders append the trailing capture group.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public abstract class StorageEntityLoaderAttributeBase : Attribute,
        IBindFromRequest, IModifyRoutePattern
    {
        /// <summary>
        /// Single-key shortcut: when the entity has exactly one key member and
        /// <see cref="Overrides"/> is unset, this overrides the wire name for
        /// that member. Ignored when the entity has multiple key members or
        /// when <see cref="Overrides"/> is set.
        /// </summary>
        public virtual string Name { get; set; }

        /// <summary>
        /// Per-key-member wire-name overrides, positional with
        /// <see cref="KeyMemberDiscovery.DiscoverKeyMembers{T}"/>'s ordering.
        /// Entries set to <c>null</c> or omitted entries fall back to the
        /// wire name declared by the member's V3 <c>[ApiProperty]</c> (or any
        /// attribute implementing <see cref="IIncludeInMemberScope{TScope}"/>
        /// for the inbound scope mapped from <see cref="Source"/>).
        /// </summary>
        public string[] Overrides { get; set; }

        protected abstract BindingSource Source { get; }

        public bool TrySelectSource(IRequestEnvelopeV3 envelope, ParameterInfo parameter,
            out BindCall call)
        {
            call = null;
            var entityType = ExtractEntityType(parameter);
            if (entityType == null) return false;

            var keyMembers = KeyMemberDiscovery.DiscoverKeyMembers(entityType);
            if (keyMembers.Length == 0) return false;

            var wireNames = ResolveWireNames(keyMembers);
            var pairs = new KeyValuePair<string, string[]>[wireNames.Length];

            var allowPath = (this.Source & BindingSource.Path) != 0;
            var allowQuery = (this.Source & BindingSource.Query) != 0;

            for (var i = 0; i < wireNames.Length; i++)
            {
                if (!TryReadRaw(envelope, wireNames[i], allowQuery, allowPath, out var raw))
                    return false;
                pairs[i] = new KeyValuePair<string, string[]>(wireNames[i], new[] { raw });
            }

            // Hand the per-key lookup over as a real envelope-derived
            // IBindingSource so the binder can recurse through ITypeBindings
            // per key part. Driver resolution is deferred to bind time via
            // ParameterSlot — no per-parameter state smuggled through the source.
            var lookupSrc = new LookupBindingSource(pairs);
            call = BindCalls.FromSource(lookupSrc, string.Empty);
            return true;
        }

        private static bool TryReadRaw(IRequestEnvelopeV3 envelope, string wireName,
            bool allowQuery, bool allowPath, out string raw)
        {
            // Path captures take precedence when both are allowed — matches the
            // V2 loader's BindingSource.Query|Path semantics where a route value
            // outranks a query value when both are present.
            if (allowPath && envelope.Route != null
                && envelope.Route.TryGetValue(wireName, out var routeVal)
                && routeVal.HasBlackSpace())
            {
                raw = routeVal;
                return true;
            }
            if (allowQuery && envelope.Query != null
                && envelope.Query.TryGetValue(wireName, out var values)
                && values is { Length: > 0 }
                && values[0].HasBlackSpace())
            {
                raw = values[0];
                return true;
            }
            raw = null;
            return false;
        }

        internal string[] ResolveWireNames(MemberInfo[] keyMembers)
        {
            var overrides = this.Overrides;
            var legacySingleName = this.Name;
            var total = keyMembers.Length;
            var scope = InboundWireNameResolver.ScopeFor(this.Source);
            var result = new string[total];
            for (var i = 0; i < total; i++)
                result[i] = ResolveWireName(keyMembers[i], i, total, overrides, legacySingleName, scope);
            return result;
        }

        private static string ResolveWireName(MemberInfo member, int index, int total,
            string[] overrides, string legacySingleName, Type scope)
        {
            if (overrides != null && index < overrides.Length && overrides[index].HasBlackSpace())
                return overrides[index];
            if (total == 1 && legacySingleName.HasBlackSpace())
                return legacySingleName;
            var wireName = InboundWireNameResolver.Resolve(member, scope);
            if (wireName.HasBlackSpace())
                return wireName;
            throw new InvalidOperationException(
                $"Storage key member '{member.DeclaringType?.FullName}.{member.Name}' is missing " +
                $"a V3 binding attribute for inbound scope `{scope.Name}`. " +
                $"Add `[ApiProperty(Name = \"…\")]` (or any other " +
                $"IIncludeInMemberScope<{scope.Name}> attribute) so the HTTP loader knows the wire name.");
        }

        /// <summary>
        /// Append a trailing capture group for the first key member when this
        /// loader sources from the URL path. Composite-key URLs (multiple path
        /// captures) are out of scope today — the framework only emits one
        /// capture to preserve the legacy <c>/api/Resource/{id}</c> shape.
        /// </summary>
        public string ModifyRoutePattern(MethodInfo method, ParameterInfo parameter, string currentPattern)
        {
            if ((this.Source & BindingSource.Path) == 0)
                return currentPattern;
            var entityType = ExtractEntityType(parameter);
            if (entityType == null)
                return currentPattern;
            var keyMembers = KeyMemberDiscovery.DiscoverKeyMembers(entityType);
            if (keyMembers.Length == 0)
                return currentPattern;
            var wireNames = ResolveWireNames(keyMembers);
            return RoutePattern.AppendTrailingCapture(currentPattern, wireNames[0],
                MemberValueType(keyMembers[0]));
        }

        private static Type MemberValueType(MemberInfo member) => member switch
        {
            PropertyInfo p => p.PropertyType,
            FieldInfo f => f.FieldType,
            _ => null,
        };

        private static Type ExtractEntityType(ParameterInfo parameter)
        {
            var t = parameter.ParameterType;
            if (!t.IsGenericType) return null;
            if (t.GetGenericTypeDefinition() != typeof(StorageEntity<>)) return null;
            return t.GenericTypeArguments[0];
        }
    }

    /// <summary>
    /// Loader equivalent of <c>[QueryId]</c>: pulls each key part from query or
    /// path, pre-loads the record, and binds a fully-populated
    /// <see cref="StorageEntity{T}"/>.
    /// </summary>
    public sealed class StorageEntityFromQueryIdAttribute : StorageEntityLoaderAttributeBase
    {
        protected override BindingSource Source => BindingSource.Query | BindingSource.Path;
    }

    /// <summary>
    /// Loader equivalent of <c>[QueryParameter]</c>: pulls each key part from
    /// the query string only.
    /// </summary>
    public sealed class StorageEntityFromQueryParamAttribute : StorageEntityLoaderAttributeBase
    {
        protected override BindingSource Source => BindingSource.Query;
    }

    /// <summary>
    /// Loader that pulls each key part from the route (path) only.
    /// </summary>
    public sealed class StorageEntityFromRouteAttribute : StorageEntityLoaderAttributeBase
    {
        protected override BindingSource Source => BindingSource.Path;
    }
}
