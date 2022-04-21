using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive;
using EastFive.Extensions;
using EastFive.Reflection;
using EastFive.Linq;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Collections.Generic;
using EastFive.Persistence;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Auth
{
    public struct AccountLinks
    {
        public AccountLink[] accountLinks;

        public TResult GetLinkForMethod<TResult>(IRef<Method> methodRef,
            Func<string, TResult> onMatched,
            Func<TResult> onNotMatched)
        {
            return this.accountLinks
                .Where(al => al.method.id == methodRef.id)
                .First(
                    (accountLink, next) => onMatched(accountLink.externalAccountKey),
                    () => onNotMatched());
        }

    }

    public struct AccountLink
    {
        public IRef<Method> method;
        public string externalAccountKey;
    }

    public class AccountLinksAttribute : StorageLookupAttribute, IPersistInAzureStorageTables
    {
        public override IEnumerable<IRefAst> GetLookupKeys(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues)
        {
            if (!typeof(AccountLinks).IsAssignableFrom(decoratedMember.GetPropertyOrFieldType()))
                throw new ArgumentException(
                    $"{nameof(AccountLinksAttribute)} should not be used to decorate any type other than {nameof(AccountLinks)}." +
                    $" Please modify {decoratedMember.DeclaringType.FullName}..{decoratedMember.Name}");

            var accountLinks = (AccountLinks)lookupValues
                .Where(lookupValue => lookupValue.Key.Name.Equals(decoratedMember.Name))
                .SelectValues()
                .Single();

            return accountLinks.accountLinks
                .NullToEmpty()
                .Select(
                    accountLink =>
                    {
                        return accountLink.externalAccountKey
                            .AsAstRef($"AM{accountLink.method.id.ToString("n")}");
                    });
        }

        #region IPersistInAzureStorageTables

        public string Name => "AccountLinks";

        public string GetTablePropertyName(MemberInfo member)
        {
            var tablePropertyName = this.Name;
            if (tablePropertyName.IsNullOrWhiteSpace())
                return member.Name;
            return tablePropertyName;
        }

        public KeyValuePair<string, EntityProperty>[] ConvertValue(object value, MemberInfo memberInfo)
        {
            var accountLinks = (AccountLinks)value;
            return accountLinks.accountLinks
                .NullToEmpty()
                .Select(
                    accountLink =>
                    {
                        var key = $"AM{accountLink.method.id.ToString("n")}";
                        var value = EntityProperty.GeneratePropertyForString(accountLink.externalAccountKey);
                        return key.PairWithValue(value);
                    })
                .ToArray();
        }

        public object GetMemberValue(MemberInfo memberInfo, IDictionary<string, EntityProperty> values)
        {
            var accountLinks = values
                .TryWhere(
                    (KeyValuePair<string, EntityProperty> kvp, out AccountLink accountLink) =>
                    {
                        if (kvp.Key.Length < 2)
                        {
                            accountLink = default;
                            return false;
                        }

                        var methodIdStr = kvp.Key.Substring(2);
                        if (!Guid.TryParse(methodIdStr, out Guid methodId))
                        {
                            accountLink = default;
                            return false;
                        }

                        if (kvp.Value.PropertyType != EdmType.String)
                        {
                            accountLink = default;
                            return false;
                        }

                        var externalKey = kvp.Value.StringValue;
                        accountLink = new AccountLink
                        {
                            method = methodId.AsRef<Method>(),
                            externalAccountKey = externalKey,
                        };
                        return true;
                    })
                .Select(tpl => tpl.@out)
                .ToArray();

            return new AccountLinks
            {
                accountLinks = accountLinks,
            };
        }


        #endregion
    }

}

