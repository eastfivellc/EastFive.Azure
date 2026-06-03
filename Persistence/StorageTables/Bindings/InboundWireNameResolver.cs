using System;
using System.Reflection;

using EastFive.Api;
using EastFive.Api.Binding.Scopes;
using EastFive.Reflection;
using EastFive.Serialization.Binding;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// Resolves the inbound wire-name of a storage-key member from the V3
    /// <see cref="IIncludeInMemberScope{TScope}"/> attribute family.
    /// Only the inbound scopes participate — <see cref="RequestBody"/>,
    /// <see cref="PatchBody"/>, and <see cref="QueryString"/>. The outbound
    /// <c>ResponseBody</c> scope is intentionally excluded: storage loaders
    /// only care where request values come from.
    /// </summary>
    internal static class InboundWireNameResolver
    {
        /// <summary>
        /// Map a loader's <see cref="BindingSource"/> flag set to the V3
        /// inbound scope that names the value on the wire. Path captures
        /// share the <see cref="QueryString"/> scope (both are URL-derived).
        /// PATCH is not its own loader source — partial updates use the
        /// query-id loader for entity lookup and represent the mutation as
        /// a separate body parameter.
        /// </summary>
        public static Type ScopeFor(BindingSource source)
        {
            if ((source & (BindingSource.Query | BindingSource.Path)) != 0)
                return typeof(QueryString);
            if ((source & BindingSource.Body) != 0)
                return typeof(RequestBody);
            throw new InvalidOperationException(
                $"No inbound wire-name scope mapped for binding source `{source}`. " +
                $"Expected one of Query, Path, or Body.");
        }

        /// <summary>
        /// Look up the wire name for <paramref name="member"/> under
        /// <paramref name="scope"/>. Returns <c>null</c> if the member
        /// carries no <see cref="IIncludeInMemberScope{TScope}"/> attribute
        /// for that scope — callers decide how to surface the miss.
        /// </summary>
        public static string Resolve(MemberInfo member, Type scope)
        {
            if (scope == typeof(QueryString))  return ResolveCore<QueryString>(member);
            if (scope == typeof(RequestBody))  return ResolveCore<RequestBody>(member);
            if (scope == typeof(PatchBody))    return ResolveCore<PatchBody>(member);
            throw new InvalidOperationException(
                $"Unsupported inbound scope `{scope.FullName}`. " +
                $"Expected one of {nameof(QueryString)}, {nameof(RequestBody)}, or {nameof(PatchBody)}.");
        }

        private static string ResolveCore<TScope>(MemberInfo member)
            where TScope : IMemberScope
            => member.TryGetAttributeInterface<IIncludeInMemberScope<TScope>>(out var s)
                ? s.GetWireName(member)
                : null;
    }
}
