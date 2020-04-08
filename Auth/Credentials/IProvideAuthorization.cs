using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EastFive.Azure.Auth
{
    public interface IProvideAuthorization
    {
        //CredentialValidationMethodTypes Method { get; }
        
        Type CallbackController { get; }

        Guid Id { get; }

        string Method { get; }

        /// <summary>
        /// This method validates that the provided token is valid for
        /// the specified username and provider.
        /// </summary>
        /// <param name="providerId">Identifies the provider to use</param>
        /// <param name="username">Identifiers the user in the remote system</param>
        /// <param name="token">Token/password to use to authenticate the user</param>
        /// <param name="success"></param>
        /// <param name="invalidCredentials"></param>
        /// <param name="couldNotConnect"></param>
        /// <returns>Value which will be stored for future access to this system. The return value must
        /// not be a default or empty string if the token was valid.</returns>
        Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> responseParams,
            Func<string, // Subject: unique identifier in the external system
                Guid?, // stateId: If the login flow was initiated from a sessionId or integrationId, this is that ID, otherwise default(Guid?)
                Guid?, // loginId: legacy GUID lookup predating the subject string identifier
                IDictionary<string, string>, // full set of parameters returned from the authorization process, saved for later.
                TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onNotAuthenticated,
            Func<string, TResult> onInvalidToken,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure);

        TResult ParseCredentailParameters<TResult>(IDictionary<string, string> responseParams,
            Func<string, Guid?, Guid?, TResult> onSuccess,
            Func<string, TResult> onFailure);

    }
}
