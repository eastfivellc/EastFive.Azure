using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

using EastFive.Security.SessionServer.Persistence;
using EastFive.Api.Services;
using EastFive.Security.SessionServer;
using EastFive.Serialization;
using EastFive.Azure.Auth;
using EastFive.Extensions;

namespace EastFive.Azure.Auth.CredentialProviders
{
    [IntegrationName(IntegrationName)]
    public class TokenCredentialProvider : IProvideAuthorization, IProvideLoginManagement
    {
        public const string IntegrationName = "Token";
        public string Method => IntegrationName;
        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

        private const string subjectIdKey = "loginId";

        private EastFive.Security.SessionServer.Persistence.DataContext dataContext;

        public TokenCredentialProvider()
        {
            this.dataContext = new DataContext(EastFive.Azure.AppSettings.ASTConnectionStringKey);
        }

        [IntegrationName(IntegrationName)]
        public static Task<TResult> InitializeAsync<TResult>(
            Func<IProvideAuthorization, TResult> onProvideAuthorization,
            Func<TResult> onProvideNothing,
            Func<string, TResult> onFailure)
        {
            return onProvideAuthorization(new TokenCredentialProvider()).AsTask();
        }
        
        public Type CallbackController => typeof(TokenCredentialProvider);

        public Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> extraParams,
            Func<string, Guid?, Guid?, IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            var token = string.Empty; // TODO: Find value from URL generator
            return this.dataContext.CredentialMappings.FindTokenCredentialByTokenAsync(Guid.Parse(token),
                (inviteId, actorId, loginId) =>
                {
                    if (!loginId.HasValue)
                        return onInvalidCredentials("Token is not connected to an account");

                    var subjectId = loginId.Value.ToString("N");
                    return onSuccess(subjectId, default(Guid?), loginId.Value,
                        new Dictionary<string, string>()
                        {
                            { TokenCredentialProvider.subjectIdKey, subjectId  }
                        });
                },
                () => onInvalidCredentials("The token does not exists"));
        }

        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> tokens,
            Func<string, Guid?, Guid?, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            var subjectId = tokens[subjectIdKey];
            var loginIdMaybe = default(Guid?);
            if (Guid.TryParse(subjectId, out Guid loginId))
                loginIdMaybe = loginId;

            return onSuccess(subjectId, default(Guid?), loginIdMaybe);
        }

        #region IProvideLoginManagement

        public Task<TResult> CreateAuthorizationAsync<TResult>(string displayName, string userId, bool isEmail, string secret, bool forceChange, Func<Guid, TResult> onSuccess, Func<Guid, TResult> usernameAlreadyInUse, Func<TResult> onPasswordInsufficent, Func<string, TResult> onServiceNotAvailable, Func<TResult> onServiceNotSupported, Func<string, TResult> onFailure)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> GetAuthorizationAsync<TResult>(Guid loginId, 
            Func<LoginInfo, TResult> onSuccess, 
            Func<TResult> onNotFound, 
            Func<string, TResult> onServiceNotAvailable, 
            Func<TResult> onServiceNotSupported, 
            Func<string, TResult> onFailure)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> GetAllAuthorizationsAsync<TResult>(Func<LoginInfo[], TResult> onFound, Func<string, TResult> onServiceNotAvailable, Func<TResult> onServiceNotSupported, Func<string, TResult> onFailure)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> UpdateAuthorizationAsync<TResult>(Guid loginId, string password, bool forceChange, Func<TResult> onSuccess, Func<string, TResult> onServiceNotAvailable, Func<TResult> onServiceNotSupported, Func<string, TResult> onFailure)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> UpdateEmailAsync<TResult>(Guid loginId, string email, Func<TResult> onSuccess, Func<string, TResult> onServiceNotAvailable, Func<TResult> onServiceNotSupported, Func<string, TResult> onFailure)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> DeleteAuthorizationAsync<TResult>(Guid loginId, Func<TResult> onSuccess, Func<string, TResult> onServiceNotAvailable, Func<TResult> onServiceNotSupported, Func<string, TResult> onFailure)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> UserParametersAsync<TResult>(Guid actorId, System.Security.Claims.Claim[] claims, IDictionary<string, string> extraParams, Func<IDictionary<string, string>, IDictionary<string, Type>, IDictionary<string, string>, TResult> onSuccess)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
