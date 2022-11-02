using System;
using System.Linq;
using System.Net.Http;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;

using EastFive;
using BlackBarLabs.Extensions;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Net;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using System.IO;
using EastFive.Serialization;

namespace EastFive.Azure.Auth.CredentialProviders
{
    public static class Sheets
    {

        [IntegrationName(IntegrationName)]
        public class Provider : IProvideAuthorization, IProvideLogin
        {
            public const string IntegrationName = "Sheets";
            public string Method => IntegrationName;
            public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

            public const string resourceTypesKey = "resource_types";
            public const string sheetIdKey = "sheet_id";

            public Type CallbackController => typeof(EastFive.Api.Azure.Resources.Content);

            public Uri GetLoginUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
            {
                return new Uri(responseControllerLocation, $"/api/SheetIntegration/?integration={state}");
            }

            public Uri GetLogoutUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
            {
                return default(Uri);
            }

            public Uri GetSignupUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
            {
                return default(Uri);
            }

            public Task<TResult> RedeemTokenAsync<TResult>(
                IDictionary<string, string> responseParams, 
                Func<IDictionary<string, string>, TResult> onSuccess,
                Func<Guid?, IDictionary<string, string>, TResult> onNotAuthenticated,
                Func<string, TResult> onInvalidToken,
                Func<string, TResult> onCouldNotConnect, 
                Func<string, TResult> onUnspecifiedConfiguration,
                Func<string, TResult> onFailure)
            {
                return onSuccess(responseParams).AsTask();
            }

            public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> responseParams,
                Func<string, IRefOptional<Authorization>, TResult> onSuccess,
                Func<string, TResult> onFailure)
            {
                return onSuccess(responseParams["content_id"], GetState());

                IRefOptional<Authorization> GetState()
                {
                    if (!responseParams.TryGetValue("responseParamState", out string stateValue))
                        return RefOptional<Authorization>.Empty();

                    RefOptional<Authorization>.TryParse(stateValue, out IRefOptional<Authorization> stateId);
                    return stateId;
                }
            }

            public Task<TResult> UserParametersAsync<TResult>(Guid actorId, System.Security.Claims.Claim[] claims, 
                IDictionary<string, string> extraParams,
                Func<IDictionary<string, string>, IDictionary<string, Type>, IDictionary<string, string>, TResult> onSuccess)
            {
                return onSuccess(new Dictionary<string, string>(), new Dictionary<string, Type>(), new Dictionary<string, string>()).ToTask();
            }

            [IntegrationName(IntegrationName)]
            public static Task<TResult> InitializeProviderAsync<TResult>(
                Func<IProvideAuthorization, TResult> onProvideAuthorization,
                Func<TResult> onProvideNothing,
                Func<string, TResult> onFailure)
            {
                var provider = new Provider();
                return onProvideAuthorization(provider).ToTask();
            }

            
        }

        //internal static async Task<TResult> GetAsync<TResult>(Guid authenticationRequestId, Func<Type, Uri> callbackUrlFunc,
        //    Func<Session, TResult> onSuccess,
        //    Func<TResult> onNotFound,
        //    Func<string, TResult> onFailure)
        //{
        //    return await await this.dataContext.AuthenticationRequests.FindByIdAsync(authenticationRequestId,
        //        async (authenticationRequestStorage) =>
        //        {
        //            return await Context.GetLoginProvider(authenticationRequestStorage.method,
        //                async (provider) =>
        //                {
        //                    var extraParams = authenticationRequestStorage.extraParams;
        //                    return await provider.UserParametersAsync(authenticationRequestStorage.authorizationId.Value, null, extraParams,
        //                        (labels, types, descriptions) =>
        //                        {
        //                            var callbackUrl = callbackUrlFunc(provider.CallbackController);
        //                            var loginUrl = provider.GetLoginUrl(authenticationRequestId, callbackUrl);
        //                            var authenticationRequest = Convert(authenticationRequestStorage, loginUrl, extraParams, labels, types, descriptions);
        //                            return onSuccess(authenticationRequest);
        //                        });
        //                },
        //                () => onFailure("The credential provider for this request is no longer enabled in this system").ToTask(),
        //                (why) => onFailure(why).ToTask());
        //        },
        //        ()=> onNotFound().ToTask());
        //}
    }
}
