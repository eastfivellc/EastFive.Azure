using System;
using System.Linq;
using EastFive.Azure.Login;
using EastFive.Extensions;

namespace EastFive.Azure.Auth.Salesforce
{
	public static  class Extensions
	{
		public static Driver GetSalesforceDriver(this Authorization authorization)
        {
            var authToken = authorization.parameters[SalesforceTokenResponse.tokenParamAccessToken];
            var instanceUrl = authorization.parameters[SalesforceTokenResponse.tokenParamInstanceUrl];
            var tokenType = authorization.parameters[SalesforceTokenResponse.tokenParamTokenType];
            var refreshToken = authorization.parameters[SalesforceTokenResponse.tokenParamRefreshToken];
            return new Driver(authorization.authorizationRef,
                instanceUrl, authToken: authToken, tokenType: tokenType, refreshToken: refreshToken);
        }

        public static TResult GetSalesforceIdentifier<TResource, TResult>(this TResource resource,
            Func<string, TResult> onIdentified,
            Func<TResult> onNoIdentification = default)
        {
            var originalType = typeof(TResource);
            var attr = originalType.GetAttributeInterface<IDefineSalesforceApiPath>();
            var (member, identifierDefinition) = originalType
                .GetPropertyAndFieldsWithAttributesInterface<IDefineSalesforceIdentifier>()
                .Single();
            return identifierDefinition.GetIdentifier(resource, member,
                onIdentified: onIdentified,
                onNoIdentification:() =>
                {
                    if(onNoIdentification.IsNotDefaultOrNull())
                        return onNoIdentification();

                    var msg = $"{originalType.FullName} does not have a Salesforce identifier.";
                    throw new Exception(msg);
                });
        }
    }
}

