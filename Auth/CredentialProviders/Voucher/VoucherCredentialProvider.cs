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
        public const string AccountKeyParameterName = "account_key";
        public const string StateParameterName = "state";

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

        public Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> responseParams,
            Func<IDictionary<string, string>, TResult> onSuccess,
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

            var token = responseParams["token"]; // TODO: Figure out real value (token is placeholder)
            return EastFive.Azure.Auth.Voucher.Utilities.ValidateToken(token,
                (accountId) =>
                {
                    var accountKey = accountId.ToString("N");
                    var updatedParams = new Dictionary<string, string>()
                    {
                        { AccountKeyParameterName, accountKey }
                    };
                    return onSuccess(updatedParams);
                },
                (errorMessage) => onInvalidCredentials(errorMessage),
                (errorMessage) => onInvalidCredentials(errorMessage),
                (errorMessage) => onInvalidCredentials(errorMessage),
                onUnspecifiedConfiguration).AsTask();
        }

        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> responseParams,
            Func<string, IRefOptional<Authorization>, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            if (!responseParams.TryGetValue(AccountKeyParameterName, out string accountKey))
                return onFailure($"{AccountKeyParameterName} is missing");
            var state = GetState();
            return onSuccess(accountKey, state);

            IRefOptional<Authorization> GetState()
            {
                if (!responseParams.TryGetValue(StateParameterName, out string stateValue))
                    return RefOptional<Authorization>.Empty();

                RefOptional<Authorization>.TryParse(stateValue, out IRefOptional<Authorization> stateId);
                return stateId;
            }
        }
    }
}
