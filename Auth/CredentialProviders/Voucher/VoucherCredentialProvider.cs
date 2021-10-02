using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using EastFive.Serialization;
using EastFive.Extensions;

namespace EastFive.Azure.Auth.CredentialProviders
{
    [IntegrationName(IntegrationName)]
    public class VoucherCredentialProvider : IProvideAuthorization
    {
        public const string IntegrationName = "Voucher";
        public string Method => IntegrationName;
        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

        [IntegrationName(IntegrationName)]
        public static Task<TResult> InitializeAsync<TResult>(
            Func<IProvideAuthorization, TResult> onProvideAuthorization,
            Func<TResult> onProvideNothing,
            Func<string, TResult> onFailure)
        {
            return onProvideAuthorization(new VoucherCredentialProvider()).AsTask();
        }
        
        public Type CallbackController => typeof(VoucherCredentialProvider);

        public Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> extraParams,
            Func<string, Guid?, Guid?, IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            //var trustedProvider = Utilities.GetTrustedProviderId();
            //var trimChars = new char[] { '/' };
            //if (String.Compare(providerId.AbsoluteUri.TrimEnd(trimChars), trustedProvider.AbsoluteUri.TrimEnd(trimChars)) != 0)
            //    return invalidCredentials("ProviderId given does not match trustred ProviderId");

            var token = extraParams["token"]; // TODO: Figure out real value (token is placeholder)
            return EastFive.Azure.Auth.Voucher.Utilities.ValidateToken(token,
                (stateId) =>
                {
                    return onSuccess(stateId.ToString("N"), stateId, default(Guid?), null);
                },
                (errorMessage) => onInvalidCredentials(errorMessage),
                (errorMessage) => onInvalidCredentials(errorMessage),
                (errorMessage) => onInvalidCredentials(errorMessage),
                onUnspecifiedConfiguration).AsTask();
        }

        public Task<TResult> UserParametersAsync<TResult>(Guid actorId, System.Security.Claims.Claim[] claims, IDictionary<string, string> extraParams, Func<IDictionary<string, string>, IDictionary<string, Type>, IDictionary<string, string>, TResult> onSuccess)
        {
            throw new NotImplementedException();
        }

        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> responseParams, Func<string, Guid?, Guid?, TResult> onSuccess, Func<string, TResult> onFailure)
        {
            throw new NotImplementedException();
        }
    }
}
