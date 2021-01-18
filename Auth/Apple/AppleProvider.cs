using EastFive.Security.CredentialProvider;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using EastFive.Security.SessionServer.Persistence;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using EastFive.Api.Services;
using System.Security.Claims;
using EastFive.Security.SessionServer;
using System.Collections;
using EastFive.Serialization;
using EastFive.Web.Configuration;
using EastFive.Extensions;
using EastFive.Api.Controllers;
using EastFive.Linq;
using EastFive.Collections.Generic;
using EastFive.Azure.Auth;
using EastFive;
using EastFive.Api;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using EastFive.Api.Azure.Credentials;
using EastFive.Api.Azure;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Auth.CredentialProviders;

namespace EastFive.Azure.Auth
{
    [IntegrationName(AppleProvider.IntegrationName)]
    public class AppleProvider : IProvideLogin, IProvideSession
    {
        public const string IntegrationName = "Apple";
        public string Method => IntegrationName;
        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

        private const string appleAuthServerUrl = "https://appleid.apple.com/auth/authorize";
        private const string appleKeyServerUrl = "https://appleid.apple.com/auth/keys";

        #region Request pararmeters
        private const string requestParamClientId = "client_id";
        private const string requestParamRedirectUri = "redirect_uri";
        private const string requestParamResponseType = "response_type";
        private const string requestParamScope = "scope";
        private const string requestParamResponseMode = "response_mode";
        private const string requestParamState = "state";
        private const string requestParamNonce = "nonce";
        #endregion

        #region Parameter that come back on the redirect
        public const string responseParamCode = "code";
        public const string responseParamState = "state";
        public const string responseParamIdToken = "id_token";
        public const string responseParamUser = "user";
        #endregion

        public AppleProvider()
        {
        }

        [IntegrationName(AppleProvider.IntegrationName)]
        public static Task<TResult> InitializeAsync<TResult>(
            Func<IProvideAuthorization, TResult> onProvideAuthorization,
            Func<TResult> onProvideNothing,
            Func<string, TResult> onFailure)
        {
            return onProvideAuthorization(new AppleProvider()).AsTask();
        }

        public Type CallbackController => typeof(AppleRedirect);


        public class Keys
        {
            public Key[] keys { get; set; }
        }

        public class Key
        {
            public string kty { get; set; }
            public string kid { get; set; }
            public string use { get; set; }
            public string alg { get; set; }
            public string n { get; set; }
            public string e { get; set; }
        }

        public virtual async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> responseParams,
            Func<string, Guid?, Guid?, IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            if (!responseParams.ContainsKey(AppleProvider.responseParamIdToken))
                return onInvalidCredentials($"`{AppleProvider.responseParamIdToken}` code was not provided");
            var idTokenJwt = responseParams[AppleProvider.responseParamIdToken];

            return await AppSettings.Auth.Apple.ValidAudiences.ConfigurationString(
                async (validAudiencesStr) =>
                {
                    var validAudiences = validAudiencesStr.Split(','.AsArray());
                    using (var httpClient = new HttpClient())
                    {
                        var keysUrl = new Uri(appleKeyServerUrl);

                        var request = new HttpRequestMessage(HttpMethod.Get, keysUrl);
                        try
                        {
                            var response = await httpClient.SendAsync(request);
                            var content = await response.Content.ReadAsStringAsync();
                            if (!response.IsSuccessStatusCode)
                                return onFailure(content);
                            try
                            {
                                var keys = JsonConvert.DeserializeObject<Keys>(content);

                                return Parse(idTokenJwt, validAudiences, keys, responseParams,
                                    (subject, authorizationId, extraParamsWithTokenValues) =>
                                        onSuccess(subject, authorizationId, default(Guid?), extraParamsWithTokenValues),
                                    (why) => onFailure(why));
                            }
                            catch (Newtonsoft.Json.JsonReaderException)
                            {
                                return onCouldNotConnect($"Apple returned non-json response:{content}");
                            }
                        }
                        catch (System.Net.Http.HttpRequestException ex)
                        {
                            return onCouldNotConnect($"{ex.GetType().FullName}:{ex.Message}");
                        }
                        catch (Exception exGeneral)
                        {
                            return onCouldNotConnect(exGeneral.Message);
                        }
                    }
                },
                (why) => onUnspecifiedConfiguration(why).AsTask());
        }

        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> responseParams,
            Func<string, Guid?, Guid?, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            return GetSubject(
                subject =>
                {
                    var state = responseParams.ContainsKey(responseParamState) ?
                           Guid.TryParse(responseParams[responseParamState], out Guid stateParsedGuid) ?
                               stateParsedGuid
                               :
                               default(Guid?)
                           :
                           default(Guid?);

                    return onSuccess(subject, state, default(Guid?));
                });

            TResult GetSubject(Func<string, TResult> callback)
            {
                if(responseParams.ContainsKey("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"))
                {
                    var subject = responseParams["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"];
                    if(subject.HasBlackSpace())
                        return callback(subject);
                }
                if(responseParams.ContainsKey(AppleProvider.responseParamIdToken))
                {
                    var jwtEncodedString = responseParams[AppleProvider.responseParamIdToken];
                    var handler = new JwtSecurityTokenHandler();
                    var token = handler.ReadJwtToken(jwtEncodedString);
                    return token.Claims
                        .Where(claim => claim.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")
                        .First(
                            (claim, next) => callback(claim.Value),
                            () => onFailure("No claim http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"));
                }

                return onFailure($"Could not locate http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier in params or claims.");
            }
        }

        private static TResult Parse<TResult>(string jwtEncodedString,
                string[] validAudiences, Keys keys, IDictionary<string, string> responseParams,
            Func<string, Guid?, IDictionary<string, string>, TResult> onSuccess,
            Func<string, TResult> onInvalidToken)
        {
            // From: https://developer.apple.com/documentation/signinwithapplerestapi/verifying_a_user
            // To verify the identity token, your app server must:
            // * Verify the JWS E256 signature using the server’s public key
            // * Verify the nonce for the authentication
            // * Verify that the iss field contains https://appleid.apple.com
            // * Verify that the aud field is the developer’s client_id
            // * Verify that the time is earlier than the exp value of the token

            var issuer = "https://appleid.apple.com";
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwtEncodedString);

            return DecodeRSA(token.Header.Kid, keys,
                rsaParams =>
                {
                    var validationParameters = new TokenValidationParameters()
                    {
                        ValidateAudience = true,
                        ValidIssuer = issuer,
                        ValidAudiences = validAudiences,
                        IssuerSigningKey = new RsaSecurityKey(rsaParams),
                        RequireExpirationTime = true,
                    };

                    try
                    {
                        var principal = handler.ValidateToken(jwtEncodedString, validationParameters,
                            out SecurityToken validatedToken);

                        var claims = principal.Claims.ToArray();
                        var subject = claims
                            .Where(claim => claim.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")
                            .First().Value;
                        var state = responseParams.ContainsKey(responseParamState) ?
                            Guid.TryParse(responseParams[responseParamState], out Guid stateParsedGuid) ?
                                stateParsedGuid
                                :
                                default(Guid?)
                            :
                            default(Guid?);

                        var updatedArgs = claims
                            .Select(claim => claim.Type.PairWithValue(claim.Value))
                            .Concat(responseParams)
                            .Distinct(kvp => kvp.Key)
                            .ToDictionary();

                        return onSuccess(subject, state, updatedArgs);
                    }
                    catch (ArgumentException ex)
                    {
                        return onInvalidToken(ex.Message);
                    }
                    catch (Microsoft.IdentityModel.Tokens.SecurityTokenInvalidIssuerException ex)
                    {
                        return onInvalidToken(ex.Message);
                    }
                    catch (Microsoft.IdentityModel.Tokens.SecurityTokenExpiredException ex)
                    {
                        return onInvalidToken(ex.Message);
                    }
                    catch (Microsoft.IdentityModel.Tokens.SecurityTokenException ex)
                    {
                        return onInvalidToken(ex.Message);
                    }
                },
                () => onInvalidToken("Key does not match Apple auth tokens"));


        }

        private static TResult DecodeRSA<TResult>(string keyId, Keys keys,
            Func<RSAParameters, TResult> onDecoded,
            Func<TResult> onNoMatch)
        {
            return keys.keys
                .Where(key => key.kid == keyId)
                .First(
                    (key, next) =>
                    {
                        var parameters = new RSAParameters
                        {
                            Exponent = Base64UrlEncoder.DecodeBytes(key.e),
                            Modulus = Base64UrlEncoder.DecodeBytes(key.n),
                        };
                        return onDecoded(parameters);
                    },
                    onNoMatch);
        }

        #region IProvideLogin

        public Uri GetLogoutUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return default(Uri);
        }

        public Uri GetLoginUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return AppSettings.Auth.Apple.ClientId.ConfigurationString(
                applicationId =>
                {
                    var tokenUrl = new Uri(appleAuthServerUrl)
                                .AddQueryParameter(requestParamClientId, applicationId)
                                .AddQueryParameter(requestParamResponseMode, "form_post")
                                .AddQueryParameter(requestParamResponseType, $"{responseParamCode} {responseParamIdToken}")
                                .AddQueryParameter(requestParamScope, "name email")
                                .AddQueryParameter(requestParamState, state.ToString("N"))
                                .AddQueryParameter(requestParamNonce, Guid.NewGuid().ToString("N"))
                                .AddQueryParameter(requestParamRedirectUri, responseControllerLocation.AbsoluteUri);
                    return tokenUrl;
                });
        }

        public Uri GetSignupUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return default(Uri);
        }

        public Task<bool> SupportsSessionAsync(EastFive.Azure.Auth.Session session)
        {
            return true.AsTask();
        }

        #endregion

        #region IProvideAccountInformation

        protected (string, string) ParseAccountInfo(IDictionary<string, string> extraParameters)
        {
            var name = "Apple User";
            var email = string.Empty;
            if (extraParameters.ContainsKey(responseParamUser))
            {
                var userInfoJson = extraParameters[responseParamUser];
                var userInfo = JsonConvert.DeserializeObject<UserInfo>(userInfoJson);
                name = $"{userInfo.name.firstName} {userInfo.name.lastName}";
                email = userInfo.email;
            }
            if (extraParameters.ContainsKey(UserInfo.NamePropertyName))
            {
                name = extraParameters[UserInfo.NamePropertyName];
            }
            if (extraParameters.ContainsKey(UserInfo.EmailPropertyName))
            {
                email = extraParameters[UserInfo.EmailPropertyName];
            }
            return (name, email);
        }

        /// <summary>
        /// {"name":{"firstName":"Joshua","middleName":"","lastName":"Wingstrom"},"email":"NOTxdvaa1g6zj@privaterelay.appleid.com"}
        /// </summary>
        public class UserInfo
        {
            public const string NamePropertyName = "name";
            public Name name { get; set; }

            public const string EmailPropertyName = "email";
            public string email { get; set; }
        }

        public class Name
        {
            public string firstName { get; set; }
            public string middleName { get; set; }
            public string lastName { get; set; }
        }

        #endregion 
    }


    public class AppleProviderAttribute : Attribute, IProvideLoginProvider
    {
        public TResult ProvideLoginProvider<TResult>(
            Func<IProvideLogin, TResult> onLoaded,
            Func<string, TResult> onNotAvailable)
        {
            var appleProvider = new AppleProvider();
            return onLoaded(appleProvider);
            //return AppSettings.ClientKey.ConfigurationGuid(
            //    (clientKey) =>
            //    {
            //        return AppSettings.ClientSecret.ConfigurationString(
            //            (clientSecret) =>
            //            {
            //                var provider = new CredentialProvider(clientKey, clientSecret);
            //                return onLoaded(provider);
            //            },
            //            onNotAvailable);
            //    },
            //    onNotAvailable);
        }
    }
}
