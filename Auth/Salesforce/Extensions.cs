using System;
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
            return new Driver(instanceUrl, authToken: authToken, tokenType: tokenType, refreshToken: refreshToken);
        }

	}
}

