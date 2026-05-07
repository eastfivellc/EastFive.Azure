using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Api.Bindings;
using EastFive.Api.Resources;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Reflection;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// Base for the loader attributes
    /// (<c>[StorageEntityFromQueryId]</c>, <c>[StorageEntityFromQueryParam]</c>,
    /// <c>[StorageEntityFromRoute]</c>) that pre-load a record into a
    /// <see cref="StorageEntity{T}"/> parameter slot.
    ///
    /// Two-step pipeline (see <see cref="StorageEntityLoaderHelpers"/>):
    ///   1. <see cref="IProvideBindingRequirements.GetParameterBinding"/>
    ///      returns one <see cref="BindingRequirement"/> per key member of
    ///      the entity type plus an <see cref="AssembleParameter"/> closure.
    ///      The closure (a) builds a partial <see cref="StorageEntity{T}"/>
    ///      (key only, entity = default) for the parameter slot and
    ///      (b) publishes the wire-level (name, value) bindings as the
    ///      per-parameter binding context for diagnostic 404 messages.
    ///   2. <see cref="IValidateHttpRequestForBoundParameters.ValidateBoundParametersForRequest"/>
    ///      performs the async load via the driver resolved through
    ///      <see cref="StorageDriverScope"/>; its returned
    ///      <see cref="ParameterMutation"/> hands the chain a parameter list
    ///      with this owner's slot replaced by the fully-loaded entity.
    ///
    /// MIXING CONCERN: this attribute is for the current routing path only.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public abstract class StorageEntityLoaderAttributeBase : Attribute,
        IBindApiValue, IDocumentParameter, IProvideBindingRequirements,
        IValidateHttpRequestForBoundParameters, IModifyRoutePattern
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
        /// Entries set to <c>null</c> or omitted entries fall back to each
        /// member's <see cref="IProvideApiValue.GetPropertyName(MemberInfo)"/>.
        /// </summary>
        public string[] Overrides { get; set; }

        protected abstract BindingSource Source { get; }

        public virtual string GetKey(ParameterInfo paramInfo)
        {
            if (this.Name.HasBlackSpace())
                return this.Name;
            return paramInfo.Name;
        }

        public (IReadOnlyList<BindingRequirement> requirements, AssembleParameter assemble)
            GetParameterBinding(ParameterInfo parameter)
        {
            var entityType = StorageEntityLoaderHelpers.ExtractEntityType(parameter);
            if (entityType == null)
                throw new InvalidOperationException(
                    $"parameter '{parameter.Name}' must be StorageEntity<T> where T : IReferenceable");

            // Per-parameter precomputation closed over by the requirements'
            // converters and the assemble closure: discover key members once,
            // resolve wire names once, capture the binding source.
            var keyMembers = KeyMemberDiscovery.DiscoverKeyMembers(entityType);
            var wireNames = ResolveWireNames(keyMembers);
            var source = this.Source;

            var requirements = keyMembers
                .Select((km, idx) =>
                {
                    var memberType = km.Member.GetPropertyOrFieldType();
                    return new BindingRequirement(
                            path: wireNames[idx],
                            source: source,
                            parameter: parameter,
                            isOptional: false)
                        .AddConverter<string>((raw, param, app, req, onParsed, onFailure) =>
                        {
                            return StorageEntityLoaderHelpers.BindKeyMemberFromString(
                                raw, km, memberType, app, req,
                                onSuccess: value => onParsed(value),
                                onFailure: msg => onFailure(msg));
                        });
                })
                .ToList();

            // Assemble produces:
            //  - value:   partial StorageEntity<T> (key only). Validator's
            //             mutation replaces it with the loaded full form
            //             before continuing the chain.
            //  - context: wire-level (name, value) bindings, used by the
            //             post-bind validator for diagnostic 404 messages.
            AssembleParameter assemble = boundValues =>
            {
                var partial = StorageEntityLoaderHelpers.BuildPartialStorageEntity(
                    entityType, boundValues);
                var bindings = StorageEntityLoaderHelpers.BuildBindings(
                    wireNames, boundValues);
                return (partial, bindings);
            };

            return (requirements, assemble);
        }

        private string[] ResolveWireNames(KeyMemberDiscovery.KeyMember[] keyMembers)
        {
            var overrides = this.Overrides;
            var legacySingleName = this.Name;
            var total = keyMembers.Length;
            return keyMembers
                .Select((km, idx) => ResolveWireName(km, idx, total, overrides, legacySingleName))
                .ToArray();
        }

        private static string ResolveWireName(KeyMemberDiscovery.KeyMember km, int index, int total,
            string[] overrides, string legacySingleName)
        {
            if (overrides != null && index < overrides.Length && overrides[index].HasBlackSpace())
                return overrides[index];
            if (total == 1 && legacySingleName.HasBlackSpace())
                return legacySingleName;
            return km.ApiValue.GetPropertyName(km.Member);
        }

        /// <summary>
        /// Append a trailing capture group for the first key member when
        /// this loader sources from the URL path. Composite-key URLs
        /// (multiple path captures) are out of scope today — the framework
        /// will only emit one capture to preserve the legacy
        /// <c>/api/Resource/{id}</c> shape.
        /// </summary>
        public string ModifyRoutePattern(MethodInfo method, ParameterInfo parameter, string currentPattern)
        {
            if ((this.Source & BindingSource.Path) == 0)
                return currentPattern;
            var entityType = StorageEntityLoaderHelpers.ExtractEntityType(parameter);
            if (entityType == null)
                return currentPattern;
            var keyMembers = KeyMemberDiscovery.DiscoverKeyMembers(entityType);
            if (keyMembers.Length == 0)
                return currentPattern;
            var wireNames = ResolveWireNames(keyMembers);
            return RoutePattern.AppendTrailingCapture(currentPattern, wireNames[0]);
        }

        public Task<ParameterMutation> ValidateBoundParametersForRequest(
            ParameterInfo owner,
            IReadOnlyDictionary<ParameterInfo, object> bindingContexts,
            IReadOnlyList<KeyValuePair<ParameterInfo, object>> parameterSelection,
            MethodInfo method,
            IApplication httpApp,
            IHttpRequest routeData,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<KeyValuePair<string, object>> bindings = Array.Empty<KeyValuePair<string, object>>();
            if (bindingContexts != null
                && bindingContexts.TryGetValue(owner, out var ctx)
                && ctx is IReadOnlyList<KeyValuePair<string, object>> bs)
            {
                bindings = bs;
            }
            return StorageEntityLoaderHelpers.LoadEntityAsync(
                owner, bindings, parameterSelection, routeData, cancellationToken);
        }

        // The legacy path (IBindApiValue.TryCast): not supported. Loader attributes only
        // make sense with v3's BindAndInvokeAsync where a post-bind validator
        // chain can swap a partial slot for the loaded entity.
        public virtual SelectParameterResult TryCast(BindingData bindingData)
        {
            throw new NotSupportedException(
                $"{GetType().Name} requires the v3 binding pipeline " +
                $"(MethodDispatcher.BindAndInvokeAsync). " +
                $"Mark the controller's [FunctionViewController] route to use v3 dispatch.");
        }

        public virtual Parameter GetParameter(ParameterInfo paramInfo, HttpApplication httpApp)
        {
            return new Parameter(paramInfo)
            {
                Name = GetKey(paramInfo),
                Required = true,
                Where = (Source & BindingSource.Path) != 0 ? "PATH" : "QUERY",
                Type = Parameter.GetTypeName(paramInfo.ParameterType, httpApp),
                OpenApiType = Parameter.GetOpenApiTypeName(paramInfo.ParameterType, httpApp),
            };
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
