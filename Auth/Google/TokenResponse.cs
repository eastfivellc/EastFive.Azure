using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

using EastFive;
using EastFive.Extensions;
using EastFive.Net;

namespace EastFive.Azure.Auth.Google
{
    public class TokenResponse
    {
        /// <summary>
        /// A token that can be sent to a Google API.
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

        public IDictionary<string, string> AppendResponseParameters(IDictionary<string, string> responseParameters)
        {
            responseParameters.TryAdd(GoogleProvider.tokenParamAccessToken, this.access_token);
            responseParameters.TryAdd(GoogleProvider.tokenParamExpiresIn, this.expires_in.ToString());
            responseParameters.TryAdd(GoogleProvider.tokenParamScope, this.scope);
            responseParameters.TryAdd(GoogleProvider.tokenParamTokenType, this.token_type);
            responseParameters.TryAdd(GoogleProvider.tokenParamIdToken, this.id_token);
            responseParameters.TryAdd(GoogleProvider.tokenParamRefreshToken, this.refresh_token);
            return responseParameters;
        }

        public static Task<TResult> LoadAsync<TResult>(Uri tokenEndpoint,
            string code, string clientId, string clientSecret, string redirectUri,
            Func<TokenResponse, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            var postValues = new Dictionary<string, string>()
            {
                { "code", code },
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "redirect_uri", redirectUri }, // "http://localhost:54610/auth/GoogleRedirect"
                { "grant_type", "authorization_code" },
            };
            return tokenEndpoint.HttpPostFormUrlEncodedContentAsync(postValues,
                onSuccess: onSuccess,
                onFailureWithBody:(statusCode, why) => onFailure(why),
                onFailure: onFailure);
        }
    }
}

