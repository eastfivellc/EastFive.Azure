using EastFive.Api;
using EastFive.Api.Controllers;
using EastFive.Extensions;
using EastFive.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Routing;

namespace EastFive.Azure.Auth.CredentialProviders
{
    public class AdminLogin : IProvideLogin
    {
        public const string IntegrationName = "Admin";

        public string Method => IntegrationName;

        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

        public Type CallbackController => typeof(AdminLoginRedirection);

        public Uri GetLoginUrl(Guid state, Uri responseControllerLocation,
            Func<Type, Uri> controllerToLocation)
        {
            return controllerToLocation(typeof(AdminLogin))
                .AddQueryParameter("state", state.ToString("N"))
                .AddQueryParameter("redir", responseControllerLocation.AbsoluteUri);
        }

        public Uri GetLogoutUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return controllerToLocation(typeof(AdminLogin))
                .AddQueryParameter("action", "logout")
                .AddQueryParameter("state", state.ToString("N"))
                .AddQueryParameter("redir", responseControllerLocation.AbsoluteUri);
        }

        public Uri GetSignupUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return default(Uri);
        }

        public async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> responseParams,
            Func<IDictionary<string, string>, TResult> onSuccess, 
            Func<Guid?, IDictionary<string, string>, TResult> onNotAuthenticated, 
            Func<string, TResult> onInvalidToken, 
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration, 
            Func<string, TResult> onFailure)
        {
            if (!responseParams.TryGetValue(AdminLoginRedirection.tokenKey, out string token))
                return onFailure("Token not found");

            // TODO: Validate token
            throw new NotImplementedException();

            return await onSuccess(responseParams).AsTask();
        }

        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> responseParams,
            Func<string, IRefOptional<Authorization>, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            if (!responseParams.TryGetValue(AdminLoginRedirection.idKey, out string userKey))
                return onFailure("ID not found");

            var stateId = responseParams.TryGetValue(AdminLoginRedirection.stateKey, out string stateKey) ?
                Guid.Parse(stateKey).AsRefOptional<Authorization>()
                :
                RefOptional<Authorization>.Empty();

            return onSuccess(userKey, stateId);
        }

        public Task<TResult> UserParametersAsync<TResult>(Guid actorId, 
                System.Security.Claims.Claim[] claims, 
                IDictionary<string, string> extraParams,
            Func<IDictionary<string, string>, IDictionary<string, Type>, IDictionary<string, string>, TResult> onSuccess)
        {
            throw new NotImplementedException();
        }

        [HttpPost]
        public static IHttpResponse PostLogin(
                Guid authenticationId,
                HttpApplication application, IProvideUrl urlHelper,
            RedirectResponse onRedirect,
            GeneralFailureResponse onFailure)
        {
            return EastFive.Security.RSA.RSAFromConfig(EastFive.Azure.AppSettings.AdminLoginRsaKey,
                rsa =>
                {
                    using (rsa)
                    {
                        var authenticationIdBytes = authenticationId.ToByteArray();
                        var signedBytes = rsa.SignData(authenticationIdBytes, CryptoConfig.MapNameToOID("SHA512"));
                        var redirectUrl = urlHelper.GetLocation<AdminLoginRedirection>(
                            adminLoginRedir => adminLoginRedir.authenticationId.AssignQueryValue(authenticationId),
                            adminLoginRedir => adminLoginRedir.token.AssignQueryValue(signedBytes),
                            application);
                        return onRedirect(redirectUrl);
                    }
                },
                () => onFailure("missing config setting"),
                (why) => onFailure(why));
        }

        public class AdminLoginRedirection : Redirection
        {

            public const string idKey = "id";
            public const string tokenKey = "token";
            public const string stateKey = "state";

            public Guid authenticationId;

            public byte[] token { get; set; }
        }
    }
}
