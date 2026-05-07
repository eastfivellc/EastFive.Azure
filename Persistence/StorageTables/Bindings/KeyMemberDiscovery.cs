using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive.Api;
using EastFive.Reflection;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// Discovers the members of an entity type that participate in its
    /// storage key (row + partition) and pairs each with the
    /// <see cref="IProvideApiValue"/> that names it on the wire.
    /// Result is cached per type — discovery is reflection-heavy.
    /// </summary>
    /// <remarks>
    /// "Key member" = any member decorated with at least one of
    /// <c>IModifyAzureStorageTableRowKey</c> or
    /// <c>IModifyAzureStorageTablePartitionKey</c>. The same member often
    /// carries both (single-key entities) — we dedupe on
    /// <see cref="MemberInfo"/>.
    /// </remarks>
    public static class KeyMemberDiscovery
    {
        public readonly struct KeyMember
        {
            public KeyMember(MemberInfo member, IProvideApiValue apiValue)
            {
                this.Member = member;
                this.ApiValue = apiValue;
            }

            public MemberInfo Member { get; }
            public IProvideApiValue ApiValue { get; }
        }

        private static readonly ConcurrentDictionary<Type, KeyMember[]> cache
            = new ConcurrentDictionary<Type, KeyMember[]>();

        /// <summary>
        /// Enumerate the key members of <typeparamref name="T"/> in declaration
        /// order. Throws if any key member is missing an
        /// <see cref="IProvideApiValue"/> — without one we have no way to
        /// expose it as a request-bound value.
        /// </summary>
        public static KeyMember[] DiscoverKeyMembers<T>()
            => DiscoverKeyMembers(typeof(T));

        public static KeyMember[] DiscoverKeyMembers(Type entityType)
            => cache.GetOrAdd(entityType, Discover);

        private static KeyMember[] Discover(Type entityType)
        {
            // Members participating in either row or partition key — deduped.
            var rowMembers = entityType
                .GetPropertyAndFieldsWithAttributesInterface<EastFive.Persistence.IModifyAzureStorageTableRowKey>()
                .Select(t => t.Item1);
            var partMembers = entityType
                .GetPropertyAndFieldsWithAttributesInterface<EastFive.Persistence.IModifyAzureStorageTablePartitionKey>()
                .Select(t => t.Item1);

            var seen = new HashSet<MemberInfo>();
            var ordered = new List<MemberInfo>();
            foreach (var m in rowMembers.Concat(partMembers))
                if (seen.Add(m))
                    ordered.Add(m);

            return ordered
                .Select(member =>
                {
                    if (!member.TryGetAttributeInterface<IProvideApiValue>(out var apiValue))
                        throw new InvalidOperationException(
                            $"Storage key member '{entityType.FullName}.{member.Name}' is missing " +
                            $"an attribute implementing {nameof(IProvideApiValue)} (e.g. [ApiProperty]). " +
                            $"Loader attributes need a wire name for each key part.");
                    return new KeyMember(member, apiValue);
                })
                .ToArray();
        }
    }
}
