using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

using EastFive;
using EastFive.Extensions;
using EastFive.Net;

namespace EastFive.Azure.Auth.OAuth
{
    public class TokenResponse
    {
        #region Parameter that come back in the token exchange
        public const string tokenParamAccessToken = "access_token";
        public const string tokenParamExpiresIn = "expires_in";
        public const string tokenParamIdToken = "id_token";
        public const string tokenParamScope = "scope";
        public const string tokenParamTokenType = "token_type";
        public const string tokenParamRefreshToken = "refresh_token";
        #endregion

        /// <summary>
        /// OAuth token that a connected app uses to request access to a protected resource on behalf of the client application.
        /// Additional permissions in the form of scopes can accompany the access token.
        /// </summary>
        public string access_token;

        /// <summary>
        /// The remaining lifetime of the access token in seconds.
        /// </summary>
        public int expires_in;

        /// <summary>
        /// The scopes of access granted by the access_token expressed as a list of space-delimited,
        /// case-sensitive strings.
        /// </summary>
        public string scope;

        /// <summary>
        /// Identifies the type of token returned. At this time, this field always has the value Bearer
        /// </summary>
        public string token_type;

        /// <summary>
        /// A JWT that contains identity information about the user that is digitally signed by Google.
        /// </summary>
        public string id_token;

        /// <summary>
        /// This field is only present if the access_type parameter was set to offline in the authentication request.
        /// For details, see Refresh tokens.
        /// </summary>
        public string refresh_token;

        public virtual IDictionary<string, string> AppendResponseParameters(IDictionary<string, string> responseParameters)
        {
            responseParameters.TryAdd(TokenResponse.tokenParamAccessToken, this.access_token);
            responseParameters.TryAdd(TokenResponse.tokenParamExpiresIn, this.expires_in.ToString());
            responseParameters.TryAdd(TokenResponse.tokenParamScope, this.scope);
            responseParameters.TryAdd(TokenResponse.tokenParamTokenType, this.token_type);
            responseParameters.TryAdd(TokenResponse.tokenParamIdToken, this.id_token);
            responseParameters.TryAdd(TokenResponse.tokenParamRefreshToken, this.refresh_token);
            return responseParameters;
        }

        protected static Task<TResult> LoadInternalAsync<T, TResult>(Uri tokenEndpoint,
            string code, string clientId, string clientSecret, string redirectUri,
            Func<T, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            var postValues = new Dictionary<string, string>()
            {
                { "code", code },
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "redirect_uri", redirectUri },
                { "grant_type", "authorization_code" },
            };
            return tokenEndpoint.HttpPostFormUrlEncodedContentAsync(postValues,
                onSuccess: onSuccess,
                onFailureWithBody: (statusCode, why) => onFailure(why),
                onFailure: onFailure);
        }

        public static Task<TResult> LoadAsync<TResult>(Uri tokenEndpoint,
            string code, string clientId, string clientSecret, string redirectUri,
            Func<TokenResponse, TResult> onSuccess,
            Func<string, TResult> onFailure) => LoadInternalAsync<TokenResponse, TResult>(tokenEndpoint:tokenEndpoint,
                code:code, clientId: clientId, clientSecret: clientSecret, redirectUri: redirectUri,
                onSuccess: onSuccess, onFailure: onFailure);
    }
}

