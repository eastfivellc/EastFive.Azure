//using System;
//using System.Threading.Tasks;
//using Facebook;
//using System.Security.Claims;
//using System.Collections.Generic;
//using EastFive.Security.SessionServer;
//using BlackBarLabs.Extensions;
//using EastFive.Api.Azure.Credentials.Attributes;
//using EastFive.Serialization;

//namespace EastFive.Api.Azure.Credentials
//{
//    [IntegrationName(IntegrationName)]
//    public class FacebookCredentialProvider : IProvideLogin
//    {
//        public const string IntegrationName = "Facebook";
//        public string Method => IntegrationName;

//        [IntegrationName(IntegrationName)]
//        public static Task<TResult> InitializeAsync<TResult>(
//            Func<IProvideLogin, TResult> onProvideLogin,
//            Func<IProvideAuthorization, TResult> onProvideAuthorization,
//            Func<TResult> onProvideNothing,
//            Func<string, TResult> onFailure)
//        {
//            return onProvideNothing().ToTask();
//        }
        
//        public Type CallbackController => typeof(Controllers.ResponseController);

//        public async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> extraParams,
//            Func<string, Guid?, Guid?, IDictionary<string, string>, TResult> onSuccess,
//            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
//            Func<string, TResult> onInvalidCredentials,
//            Func<string, TResult> onCouldNotConnect,
//            Func<string, TResult> onUnspecifiedConfiguration,
//            Func<string, TResult> onFailure)
//        {
//            var token = extraParams["fb_response"]; // TODO: Lookup real value
//            if (String.IsNullOrWhiteSpace(token))
//                return onInvalidCredentials("accessToken is null");
//            var client = new FacebookClient(token);
//            try
//            {
//                dynamic result = await client.GetTaskAsync("me", new { fields = "name,id" });
//                if (null == result)
//                    return onInvalidCredentials("Cannot get token from Facebook");
//                var username = ""; // TODO: Lookup from database
//                if (username != result.id)
//                    return onInvalidCredentials("username and result.Id from Facebook do not match");
//                throw new NotImplementedException();
//            }
//            catch (Exception ex)
//            {
//                if (ex.Message.Contains("OAuthException"))
//                    return onInvalidCredentials("OAuthException occurred");
//                throw ex;
//            }
//        }

//        #region IProvideLogin

//        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

//        public Uri GetLoginUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
//        {
//            throw new NotImplementedException();
//        }

//        public Uri GetSignupUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
//        {
//            throw new NotImplementedException();
//        }

//        public Uri GetLogoutUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
//        {
//            throw new NotImplementedException();
//        }

//        public Task<TResult> UserParametersAsync<TResult>(Guid actorId, System.Security.Claims.Claim[] claims, IDictionary<string, string> extraParams, Func<IDictionary<string, string>, IDictionary<string, Type>, IDictionary<string, string>, TResult> onSuccess)
//        {
//            throw new NotImplementedException();
//        }

//        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> responseParams, Func<string, Guid?, Guid?, TResult> onSuccess, Func<string, TResult> onFailure)
//        {
//            throw new NotImplementedException();
//        }

//        #endregion
//    }
//}
