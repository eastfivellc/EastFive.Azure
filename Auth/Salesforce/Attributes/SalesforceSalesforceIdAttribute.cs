using System;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

using EastFive.Azure.Auth;
using EastFive.Azure.Auth.Salesforce;
using EastFive.Azure.Auth.Salesforce.Resources;
using EastFive.Reflection;
using EastFive.Linq;
using Newtonsoft.Json.Linq;
using EastFive.Extensions;

namespace EastFive.Azure.Auth.Salesforce
{
    public class SalesforceSalesforceIdAttribute : SalesforcePropertyAttribute
    {
        public SalesforceSalesforceIdAttribute(string name) : base(name) { }

        public override void PopluateSalesforceResource(JsonTextWriter jsonWriter,
            MemberInfo member, object resource, Field field)
        {
            var methodId = SalesforceProvider.IntegrationId;
            ValidateIsAccountLinks(member);

            var accountLinksObj = member.GetValue(resource);
            var accountLinks = (AccountLinks)accountLinksObj;
            bool written = accountLinks.accountLinks
                .Where(al => al.method.id == SalesforceProvider.IntegrationId)
                .First(
                    (accountLink, next) =>
                    {
                        jsonWriter.WritePropertyName(this.Name);
                        jsonWriter.WriteValue(accountLink.externalAccountKey);
                        return true;
                    },
                    () =>
                    {
                        return false;
                    });
        }

        public override void PopluateSalesforceResource(object resource,
            MemberInfo member, JObject jsonObject, JProperty jProperty,
            bool overrideEmptyValues)
        {
            ValidateIsAccountLinks(member);
            var accountLinksObj = member.GetValue(resource);
            var accountLinks = (AccountLinks)accountLinksObj;

            if (jProperty.Value.Type != JTokenType.String)
                return;

            var linkKey = jProperty.Value.Value<string>();
            if (linkKey.IsNullOrWhiteSpace())
                return;

            var accountLinksUpdated = accountLinks.AddOrUpdateCredentials(
                SalesforceProvider.IntegrationId.AsRef<Method>(),
                linkKey);
            member.SetPropertyOrFieldValue(resource, accountLinksUpdated);
        }

        private void ValidateIsAccountLinks(MemberInfo member)
        {
            var memberType = member.GetPropertyOrFieldType();
            if (!typeof(AccountLinks).IsAssignableFrom(memberType))
                throw new ArgumentException(
                    $"{nameof(SalesforceSalesforceIdAttribute)} decorates {member.DeclaringType.FullName}..{member.Name} " +
                    $"of type {memberType.FullName} is not assignable to {nameof(AccountLinks)}");
        }
    }

    public class SalesforceSalesforceId2Attribute : SalesforceSalesforceIdAttribute
    {
        public SalesforceSalesforceId2Attribute(string name) : base(name) { }
    }

    public class SalesforceIdentifier : Attribute, IDefineSalesforceIdentifier
    {
        public TResult GetIdentifier<T, TResult>(T resource, MemberInfo propertyOrField,
            Func<string, TResult> onIdentified,
            Func<TResult> onNoIdentification)
        {
            var propertyOrFieldType = propertyOrField.GetPropertyOrFieldType();
            if (typeof(AccountLinks).IsAssignableFrom(propertyOrFieldType))
            {
                var accountLinksObj = propertyOrField.GetValue(resource);
                var accountLinks = (AccountLinks)accountLinksObj;
                return accountLinks.accountLinks
                    .Where(al => al.method.id == SalesforceProvider.IntegrationId)
                    .First(
                        (accountLink, next) =>
                        {
                            return onIdentified(accountLink.externalAccountKey);
                        },
                        () =>
                        {
                            return onNoIdentification();
                        });
            }
            if (typeof(string).IsAssignableFrom(propertyOrFieldType))
            {
                var sfIdObj = propertyOrField.GetValue(resource);
                var sfId = (string)sfIdObj;
                if(sfId.HasBlackSpace())
                    return onIdentified(sfId);

                return onNoIdentification();
            }

            throw new ArgumentException($"{propertyOrField.DeclaringType.FullName}..{propertyOrField.Name} " +
                $"is decorated with {nameof(SalesforceIdentifier)} but is of type `{propertyOrFieldType.FullName}` " +
                $"which is not supported.");
        }

        public TResource SetIdentifier<TResource>(TResource resource, MemberInfo propertyOrField, string sfId)
        {
            var propertyOrFieldType = propertyOrField.GetPropertyOrFieldType();
            var propertyOrFieldObjValue = propertyOrField.GetValue(resource);
            if (typeof(AccountLinks).IsAssignableFrom(propertyOrFieldType))
            {
                var accountLinks = (AccountLinks)propertyOrFieldObjValue;
                var accountLinksUpdated = accountLinks.AddOrUpdateCredentials(
                    SalesforceProvider.IntegrationId.AsRef<Method>(), sfId);
                return (TResource)propertyOrField.SetPropertyOrFieldValue(resource, accountLinksUpdated);
            }

            if (typeof(string).IsAssignableFrom(propertyOrFieldType))
            {
                return (TResource)propertyOrField.SetPropertyOrFieldValue(resource, sfId);
            }

            throw new ArgumentException($"{propertyOrField.DeclaringType.FullName}..{propertyOrField.Name} " +
                $"is decorated with {nameof(SalesforceIdentifier)} but is of type `{propertyOrFieldType.FullName}` " +
                $"which is not supported.");
        }
    }
}

