using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive.Reflection;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// Discovers the members of an entity type that participate in its
    /// storage key (row + partition). Result is cached per type — discovery
    /// is reflection-heavy.
    /// </summary>
    /// <remarks>
    /// "Key member" = any member decorated with at least one of
    /// <c>IComputeAzureStorageTableRowKey</c> (e.g. <c>[RowKey]</c>) or
    /// <c>IComputeAzureStorageTablePartitionKey</c> (e.g. <c>[RowKeyPrefix]</c>,
    /// <c>[StandardPartitionKey]</c>). The same member often carries both
    /// (single-key entities) — we dedupe on <see cref="MemberInfo"/>.
    /// <para>
    /// Wire-name resolution for HTTP binding is intentionally NOT done here;
    /// callers consult <see cref="InboundWireNameResolver"/> with the scope
    /// appropriate to their request source. This keeps discovery decoupled
    /// from the API stack — no <c>EastFive.Api</c> dependency.
    /// </para>
    /// </remarks>
    public static class KeyMemberDiscovery
    {
        private static readonly ConcurrentDictionary<Type, MemberInfo[]> cache
            = new ConcurrentDictionary<Type, MemberInfo[]>();

        /// <summary>
        /// Enumerate the key members of <typeparamref name="T"/> in declaration
        /// order (row-key members first, then partition-key members not
        /// already seen).
        /// </summary>
        public static MemberInfo[] DiscoverKeyMembers<T>()
            => DiscoverKeyMembers(typeof(T));

        public static MemberInfo[] DiscoverKeyMembers(Type entityType)
            => cache.GetOrAdd(entityType, Discover);

        private static MemberInfo[] Discover(Type entityType)
        {
            // Members participating in either row or partition key — deduped.
            // Use the *Compute* interfaces so we identify members by what
            // produces the actual table key (RowKey, RowKeyPrefix, …) rather
            // than the orthogonal API-naming attribute family.
            var rowMembers = entityType
                .GetPropertyAndFieldsWithAttributesInterface<EastFive.Persistence.IComputeAzureStorageTableRowKey>()
                .Select(t => t.Item1);
            var partMembers = entityType
                .GetPropertyAndFieldsWithAttributesInterface<EastFive.Persistence.IComputeAzureStorageTablePartitionKey>()
                .Select(t => t.Item1);

            var seen = new HashSet<MemberInfo>();
            var ordered = new List<MemberInfo>();
            foreach (var m in rowMembers.Concat(partMembers))
                if (seen.Add(m))
                    ordered.Add(m);

            return ordered.ToArray();
        }
    }
}
