using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

using EastFive;
using EastFive.Extensions;
using EastFive.Net;

namespace EastFive.Azure.Auth.Salesforce
{
    public class SalesforceTokenResponse : OAuth.TokenResponse
    {
        // https://help.salesforce.com/s/articleView?id=sf.remoteaccess_oauth_web_server_flow.htm&type=5

        #region Parameter that come back in the token exchange
        public const string tokenParamSignature = "signature";
        public const string tokenParamContentDomain = "content_domain";
        public const string tokenParamContentSid = "content_sid";
        public const string tokenParamLightningDomain = "lightning_domain";
        public const string tokenParamLightningSid = "lightning_sid";
        public const string tokenParamVisualforceDomain = "visualforce_domain";
        public const string tokenParamVisualforceSid = "visualforce_sid";
        public const string tokenParamCsrfToken = "csrf_token";
        public const string tokenParamInstanceUrl = "instance_url";
        public const string tokenParamId = "id";
        #endregion

        /// <summary>
        /// Base64-encoded HMAC-SHA256 signature signed with the client_secret.
        /// The signature can include the concatenated ID and issued_at value,
        /// which you can use to verify that the identity URL hasn’t changed since the server sent it.
        /// </summary>
        public string signature;

        /// <summary>
        /// The domain of the content session, which maps to the content SID:
        /// MyDomainName.file.force.com → content_sid.
        /// </summary>
        public string content_domain;

        /// <summary>
        /// The SID associated with the domain of the content session.
        /// Salesforce returns a unique SID that the hybrid app directly sets in the domain’s session cookie.
        /// </summary>
        public string content_sid;

        /// <summary>
        /// The domain of the Lightning session, which maps to the Lightning SID:
        /// <MyDomainName or instance>.lightning.force.com → lightning_sid.
        /// </summary>
        public string lightning_domain;

        /// <summary>
        /// The SID associated with the domain of the Lightning session.
        /// Salesforce returns a unique SID that the hybrid app directly sets in the domain’s session cookie.
        /// </summary>
        public string lightning_sid;

        /// <summary>
        /// The domain of the Visualforce session, which maps to the Visualforce SID:
        /// MyDomainName.vf.force.com → visualforce_sid.
        /// </summary>
        public string visualforce_domain;

        /// <summary>
        /// The SID associated with the domain of the Visualforce session.
        /// Salesforce returns a unique SID that the hybrid app directly sets in the domain’s session cookie.
        /// </summary>
        public string visualforce_sid;

        /// <summary>
        /// The cross-site request forgery (CSRF) token to prevent attacks during child sessions.
        /// </summary>
        public string csrf_token;

        /// <summary>
        /// A URL indicating the instance of the user’s org.
        /// </summary>
        /// <example>https://yourInstance.salesforce.com/</example>
        public Uri instance_url;

        /// <summary>
        /// An identity URL that can be used to identify the user and to query for more information about the user.
        /// </summary>
        /// <remarks>See Identity URLs.https://help.salesforce.com/s/articleView?id=sf.remoteaccess_using_openid.htm&type=5</remarks>
        public Uri id;

        public override IDictionary<string, string> AppendResponseParameters(IDictionary<string, string> responseParameters)
        {
            var updatedResponseParameters = base.AppendResponseParameters(responseParameters);
            updatedResponseParameters.TryAdd(SalesforceTokenResponse.tokenParamSignature, this.signature);
            updatedResponseParameters.TryAdd(SalesforceTokenResponse.tokenParamContentDomain, this.content_domain);
            updatedResponseParameters.TryAdd(SalesforceTokenResponse.tokenParamContentSid, this.content_sid);
            updatedResponseParameters.TryAdd(SalesforceTokenResponse.tokenParamLightningDomain, this.lightning_domain);
            updatedResponseParameters.TryAdd(SalesforceTokenResponse.tokenParamLightningSid, this.lightning_sid);
            updatedResponseParameters.TryAdd(SalesforceTokenResponse.tokenParamVisualforceDomain, this.visualforce_domain);
            updatedResponseParameters.TryAdd(SalesforceTokenResponse.tokenParamVisualforceSid, this.visualforce_sid);
            updatedResponseParameters.TryAdd(SalesforceTokenResponse.tokenParamCsrfToken, this.csrf_token);
            if(this.instance_url.IsNotDefaultOrNull())
                updatedResponseParameters.TryAdd(SalesforceTokenResponse.tokenParamInstanceUrl, this.instance_url.OriginalString);
            if(this.id.IsNotDefaultOrNull())
                updatedResponseParameters.TryAdd(SalesforceTokenResponse.tokenParamId, this.id.OriginalString);
            return updatedResponseParameters;
        }

        public static Task<TResult> LoadTokenAsync<TResult>(Uri tokenEndpoint,
            string code, string clientId, string clientSecret, string redirectUri,
            Func<SalesforceTokenResponse, TResult> onSuccess,
            Func<string, TResult> onFailure) => OAuth.TokenResponse.LoadInternalAsync<SalesforceTokenResponse, TResult>(tokenEndpoint,
                code: code, clientId: clientId, clientSecret: clientSecret, redirectUri: redirectUri,
                onSuccess: onSuccess, onFailure: onFailure);
    }
}

