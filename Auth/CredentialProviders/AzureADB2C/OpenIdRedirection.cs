﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Api.Controllers;


namespace EastFive.Azure.Auth.CredentialProviders.AzureADB2C
{
    [FunctionViewController(
        Route = "OpenIdRedirection",
        ContentType = "x-application/auth-redirection.aadb2c",
        ContentTypeVersion = "0.1")]
    public class OpenIdRedirection : EastFive.Azure.Auth.Redirection
    {

        public const string id_token = "id_token";

        public const string state = "state";

        public string error { get; set; }

        public string error_description { get; set; }

        //[ApiProperty(PropertyName = ProvideLoginMock.extraParamState)]
        //[JsonProperty(PropertyName = ProvideLoginMock.extraParamState)]
        //public Guid? state;

        //[ApiProperty(PropertyName = ProvideLoginMock.extraParamToken)]
        //[JsonProperty(PropertyName = ProvideLoginMock.extraParamToken)]
        //public string token;

        [HttpGet(MatchAllParameters = false)]
        public static async Task<IHttpResponse> Get(
                //[QueryParameter(Name = ProvideLoginMock.extraParamState)]IRefOptional<Authorization> authorizationRef,
                //[QueryParameter(Name = ProvideLoginMock.extraParamToken)]string token,
                IAzureApplication application, IProvideUrl urlHelper,
                IInvokeApplication endpoints,
                IHttpRequest request,
            RedirectResponse onRedirectResponse,
            ServiceUnavailableResponse onNoServiceResponse,
            BadRequestResponse onBadCredentials,
            GeneralConflictResponse onFailure)
        {
            var parameters = request.RequestUri.ParseQuery();
            var method = EastFive.Azure.Auth.Method.ByMethodName(
                AzureADB2CProvider.IntegrationName, application);
            return await EastFive.Azure.Auth.Redirection.ProcessRequestAsync(method, parameters,
                    application, request, endpoints, urlHelper,
                (redirect, accountIdMaybe) => onRedirectResponse(redirect),
                (why) => onBadCredentials().AddReason($"Bad credentials:{why}"),
                (why) => onNoServiceResponse().AddReason(why),
                (why) => onFailure(why));
        }

        [HttpPost(MatchAllParameters = false)]
        public static async Task<IHttpResponse> PostAsync(
                [Property(Name = id_token)]string idToken,
                [Property(Name = state)]IRef<Authorization> authorization,
                IAzureApplication application, IProvideUrl urlHelper,
                IHttpRequest request, IInvokeApplication endpoints,
            RedirectResponse onRedirectResponse,
            ServiceUnavailableResponse onNoServiceResponse,
            BadRequestResponse onBadCredentials,
            GeneralConflictResponse onFailure)
        {
            var parameters = new Dictionary<string, string>
            {
                { id_token, idToken },
                { state, authorization.id.ToString("N") },
            };
            var method = EastFive.Azure.Auth.Method.ByMethodName(
                AzureADB2CProvider.IntegrationName, application);

            return await EastFive.Azure.Auth.Redirection.ProcessRequestAsync(method, parameters,
                    application, request, endpoints, urlHelper,
                (redirect, accountIdMaybe) => onRedirectResponse(redirect),
                (why) => onBadCredentials().AddReason($"Bad credentials:{why}"),
                (why) => onNoServiceResponse().AddReason(why),
                (why) => onFailure(why));
        }
    }
}
