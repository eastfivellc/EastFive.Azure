//using BlackBarLabs;
//using BlackBarLabs.Extensions;
//using BlackBarLabs.Linq;
//using EastFive.Api.Azure.Credentials.Attributes;
//using EastFive.Api.Azure.Credentials.Controllers;
//using EastFive.Collections.Generic;
//using EastFive.Security.SessionServer;
//using EastFive.Serialization;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net.Http;
//using System.Net.Http.Headers;
//using System.Security.Claims;
//using System.Text;
//using System.Threading.Tasks;

//namespace EastFive.Api.Azure.Credentials
//{
//    [IntegrationName(InternalProvider.IntegrationName)]
//    public class InternalProvider : IProvideLogin
//    {
//        public const string IntegrationName = "Internal";
//        public string Method => IntegrationName;
//        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

//        #region Initialization

//        public const string accountIdKey = "account_id";
//        public const string integrationIdKey = "integration_id";
//        public const string resourceTypes = "resource_types";
        
//        private InternalProvider()
//        {
//        }
        
//        public static TResult LoadFromConfig<TResult>(
//            Func<InternalProvider, TResult> onLoaded,
//            Func<string, TResult> onConfigurationNotAvailable)
//        {
//            return onLoaded(new InternalProvider());
//        }

//        [Attributes.IntegrationName(IntegrationName)]
//        public static async Task<TResult> InitializeAsync<TResult>(
//            Func<IProvideAuthorization, TResult> onProvideAuthorization,
//            Func<TResult> onProvideNothing,
//            Func<string, TResult> onFailure)
//        {
//            return await LoadFromConfig(
//                (provider) => onProvideAuthorization(provider),
//                (why) => onFailure(why)).ToTask();
//        }

//        #endregion

//        #region IProvideAuthorization
        
//        public async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> tokenParameters, 
//            Func<string, Guid?, Guid?, IDictionary<string, string>, TResult> onSuccess,
//            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
//            Func<string, TResult> onInvalidCredentials,
//            Func<string, TResult> onCouldNotConnect, 
//            Func<string, TResult> onUnspecifiedConfiguration, 
//            Func<string, TResult> onFailure)
//        {
//            if (!tokenParameters.ContainsKey(InternalProvider.integrationIdKey))
//                return onInvalidCredentials($"Missing {integrationIdKey}");
//            var integrationIdString = tokenParameters[InternalProvider.integrationIdKey];
//            if(!Guid.TryParse(integrationIdString, out Guid integrationId))
//                return onInvalidCredentials($"[{integrationIdString}] is not a UUID");

//            return await Context.LoadFromConfiguration().Integrations.GetByIdAsync(integrationId,
//                (authIdMaybe, method) =>
//                {
//                    if (!authIdMaybe.HasValue)
//                        return onInvalidCredentials("Integration was not authorized.");
//                    var subject = integrationIdString;
//                    var stateId = integrationId;
//                    var loginId = integrationId;
//                    var extraParamsWithRedemptionParams = tokenParameters
//                        .Append(InternalProvider.accountIdKey, authIdMaybe.Value.ToString("N"))
//                        .ToDictionary();
//                    return onSuccess(subject, stateId, loginId, extraParamsWithRedemptionParams);
//                },
//                () => onInvalidCredentials($"Could not find integration [{integrationId}]"));
//            // return onSuccess(, default(Guid?), default(Guid?), tokenParameters).ToTask();
//        }

//        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> tokenParameters, 
//            Func<string, Guid?, Guid?, TResult> onSuccess, 
//            Func<string, TResult> onFailure)
//        {
//            if (!tokenParameters.ContainsKey(InternalProvider.integrationIdKey))
//                return onFailure($"Missing {integrationIdKey}");
//            var integrationIdString = tokenParameters[InternalProvider.integrationIdKey];
//            if (!Guid.TryParse(integrationIdString, out Guid integrationId))
//                return onFailure($"[{integrationIdString}] is not a UUID");
            
//            var subject = integrationIdString;
//            var stateId = integrationId;
//            var loginId = integrationId;
            
//            return onSuccess(subject, stateId, loginId);
//        }

//        #endregion

//        #region IProvideLogin

//        public Type CallbackController => typeof(InternalProvider);

//        public Uri GetLoginUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
//        {
//            return controllerToLocation(typeof(EastFive.Api.Controllers.InternalIntegrationController))
//                .AddQueryParameter(EastFive.Api.Controllers.InternalIntegrationController.StateQueryParameter, state.ToString());
//                //.AddQueryParameter("redirect", responseControllerLocation.AbsoluteUri);
//        }

//        public Uri GetLogoutUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
//        {
//            return default(Uri);
//        }

//        public Uri GetSignupUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
//        {
//            return default(Uri);
//        }

//        public Task<TResult> UserParametersAsync<TResult>(Guid actorId, System.Security.Claims.Claim[] claims, 
//            IDictionary<string, string> extraParams, 
//            Func<
//                IDictionary<string, string>,
//                IDictionary<string, Type>, 
//                IDictionary<string, string>,
//                TResult> onSuccess)
//        {
//            return onSuccess(
//                    new Dictionary<string, string>()
//                    {
//                        { "AutoIntegrateProducts", "Automatically Map Products" }
//                    },
//                    new Dictionary<string, Type>()
//                    {
//                        { "AutoIntegrateProducts", typeof(bool) }
//                    },
//                    new Dictionary<string, string>()
//                    {
//                        { "AutoIntegrateProducts", "When true, the system pick the best match for products when a mapping does not exists." }
//                    })
//                .ToTask();
//        }

//        #endregion


//    }
//}
