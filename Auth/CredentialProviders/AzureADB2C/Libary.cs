using EastFive.Net;
using EastFive.Web;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.AzureADB2C
{
    public static class Libary
    {
        public static async Task<TResult> InitializeAsync<TResult>(
                string tenant, string signupFlow, string signinFlow,
                string audience,
            Func<string, string, string, TokenValidationParameters, TResult> onSuccess,
            Func<string, TResult> onFailed)
        {
            var signupFlowUrlStr = String.Format(Resources.ConfigurationResource.ConfigurationEndpoint,
                tenant, signupFlow);
            var signinFlowUrlStr = String.Format(Resources.ConfigurationResource.ConfigurationEndpoint,
                tenant, signinFlow);
            using(var httpClient = new HttpClient())
            {
                return await await GetResourceAsync(httpClient, signupFlowUrlStr,
                    async signupFlowConfig =>
                    {
                        return await await  GetResourceAsync(httpClient, signinFlowUrlStr,
                            signinFlowConfig =>
                            {
                                var signinEndpoint = signinFlowConfig.AuthorizationEndpoint;
                                var logoutEndpoint = signinFlowConfig.EndSessionEndpoint;
                                var signupEndpoint = signupFlowConfig.AuthorizationEndpoint;
                                return GetValidator(audience, signinFlowConfig,
                                    (validator) =>
                                    {
                                        return onSuccess(signupEndpoint, signinEndpoint, logoutEndpoint, validator);
                                    },
                                    (why) => onFailed(why));
                            },
                            onFailed.AsAsyncFunc());
                    },
                    onFailed.AsAsyncFunc());
            }

            async Task<SResult> GetResourceAsync<SResult>(HttpClient httpClient, string urlStr,
                Func<Resources.ConfigurationResource, SResult> onGotConfig,
                Func<string, SResult> onFailedToGetConfig)
            {
                try
                {
                    var response = await httpClient.GetAsync(urlStr);
                    var configStr = await response.Content.ReadAsStringAsync();
                    var configRes = Newtonsoft.Json.JsonConvert.DeserializeObject<Resources.ConfigurationResource>(configStr);
                    return onGotConfig(configRes);
                }
                catch (Exception ex)
                {
                    return onFailedToGetConfig(ex.Message);
                }
            }
        }

        private static async Task<TResult> GetValidator<TResult>(string audience, Resources.ConfigurationResource config,
            Func<TokenValidationParameters, TResult> onSuccess,
            Func<string, TResult> onFailed)
        {
            if (!Uri.TryCreate(config.JwksUri, UriKind.Absolute, out Uri jwksUri))
                return onFailed($"`{config.JwksUri}` is not a valid url.");

            return await jwksUri.HttpClientGetResourceAsync(
                (Resources.KeyResource keys) =>
                {
                    var validationParameters = new TokenValidationParameters();
                    validationParameters.IssuerSigningKeys = keys.GetKeys();
                    validationParameters.ValidAudience = audience; // "51d61cbc-d8bd-4928-8abb-6e1bb315552";
                    validationParameters.ValidIssuer = config.Issuer;
                    return onSuccess(validationParameters);
                },
                onFailureWithBody:(code, why) =>
                {
                    return onFailed(why);
                },
                onFailure:(why) =>
                {
                    return onFailed(why);
                });
        }
    }
}
