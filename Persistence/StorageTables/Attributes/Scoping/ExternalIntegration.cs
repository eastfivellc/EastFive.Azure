using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using EastFive;
using EastFive.Extensions;
using EastFive.Collections.Generic;
using EastFive.Serialization;
using EastFive.Linq;
using EastFive.Linq.Expressions;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class ExternalIntegrationLookupAttribute 
        : StorageLookupAttribute, IProvideIntegrationId
    {
        public bool TryGetIntegrationId(MemberInfo key, object value, out string integrationId)
        {
            var idMaybe = (Guid?)value;
            if (idMaybe.HasValue)
            {
                var id = idMaybe.Value;
                integrationId = id.ToString("N");
                return true;
            }
            integrationId = default;
            return false;
        }

        public override IEnumerable<MemberInfo> ProvideLookupMembers(MemberInfo decoratedMember)
        {
            return decoratedMember.DeclaringType
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(
                    (member) =>
                    {
                        if (member.ContainsAttributeInterface<IProvideIntegrationId>())
                            return true;
                        if (member.ContainsAttributeInterface<IProvideIntegrationKey>())
                            return true;
                        return false;
                    });
        }

        public override IEnumerable<IRefAst> GetLookupKeys(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues)
        {
            return lookupValues
                .Select(
                    lookup =>
                    {
                        var hasAttr = lookup.Key.TryGetAttributeInterface(
                            out IProvideIntegrationId integrationIdProvider);
                        return (hasAttr, lookup, integrationIdProvider);
                    })
                .Where(tpl => tpl.hasAttr)
                .First(
                    (tpl, next) =>
                    {
                        var (hasAttr, lookup, integrationIdProvider) = tpl;
                        if(!integrationIdProvider.TryGetIntegrationId(lookup.Key, lookup.Value, 
                                out string integrationId))
                            return Enumerable.Empty<IRefAst>();
                        var keyLookupAttrs = lookupValues
                            .Select(
                                lookup =>
                                {
                                    var hasAttr = lookup.Key.TryGetAttributeInterface(
                                        out IProvideIntegrationKey integrationKeyProvider);
                                    return (hasAttr, lookup, integrationKeyProvider);
                                })
                            .Where(tpl => tpl.hasAttr);
                        if (!keyLookupAttrs.Any())
                            throw new Exception(
                                $"Property that implements {nameof(IProvideIntegrationKey)} was not provided in query.");

                        return keyLookupAttrs
                            .First(
                                (tpl, next) =>
                                {
                                    var (hasAttrKey, lookupKey, integrationKeyProvider) = tpl;
                                    if (integrationKeyProvider.TryGetIntegrationKey(integrationId,
                                            lookupKey.Key, lookupKey.Value, out IRefAst astLookup))
                                        return astLookup.AsEnumerable();
                                    return next();
                                },
                                () => Enumerable.Empty<IRefAst>());
                    },
                    () =>
                    {
                        throw new Exception(
                            $"{decoratedMember.DeclaringType.FullName}..{decoratedMember.Name} was not provided in query.");
                        return Enumerable.Empty<IRefAst>();
                    });
        }

    }

    public interface IProvideIntegrationId
    {
        bool TryGetIntegrationId(MemberInfo key, object value, out string integrationId);
    }

    public interface IProvideIntegrationKey
    {
        bool TryGetIntegrationKey(string integrationId, MemberInfo key, object value,
            out IRefAst astLookup);
    }

    public class IntegrationKeyAttribute : Attribute, IProvideIntegrationKey
    {
        private uint? charactersMaybe;
        public uint Characters
        {
            get
            {
                if (!charactersMaybe.HasValue)
                    return 2;
                return charactersMaybe.Value;
            }
            set
            {
                charactersMaybe = value;
            }
        }

        public bool TryGetIntegrationKey(string integrationId, MemberInfo key, object value,
            out IRefAst astLookup)
        {
            var integrationKey = (string)value;
            if (integrationKey.IsNullOrWhiteSpace())
            {
                astLookup = default;
                return false;
            }
            var integrationKeyHashInt = integrationKey.GetBytes().HashXX64();
            var integrationKeyHash = integrationKeyHashInt.ToString("X").Substring(0, (int)this.Characters);
            var partitionKey = integrationId + integrationKeyHash;
            astLookup = new RefAst(integrationKey, partitionKey);
            return true;
        }
    }
}
