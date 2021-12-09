using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using EastFive.Azure;
using EastFive.Azure.Auth;
using EastFive.Security.SessionServer;

namespace EastFive.Api.Azure.Credentials
{
    public interface IProvideAccountInformation
    {
        Task<TResult> CreateAccount<TResult>(
                string subject, IDictionary<string, string> extraParameters,
                Method authentication, Authorization authorization, Uri baseUri,
                IApiApplication webApiApplication,
            Func<Guid, TResult> onCreatedMapping,
            Func<TResult> onAllowSelfServeAccounts,
            Func<Uri, TResult> onInterceptProcess,
            Func<TResult> onNoChange);

        Task<TResult> FindOrCreateAccountByMethodAndKeyAsync<TResult>(
                AccountLink accountLink,
                IAuthApplication application,
            Func<Guid, TResult> onFound,
            Func<TResult> onNotFound,
            Func<TResult> onNoEffect);

        Task<TResult> CreateUnpopulatedAccountAsync<TResult>(
                AccountLink accountLink,
                IAuthApplication application,
            Func<IAccount, Func<IAccount, Task>, Task<TResult>> onNeedsPopulated,
            Func<Uri, TResult> onInterupted,
            Func<string, TResult> onNotCreated,
            Func<TResult> onNoEffect);
    }

    public static class ClaimsList
    {

    }

    public interface IProvideClaims
    {
        bool GetStandardClaimValue(string claimType, IDictionary<string, string> parameters, out string claimValue);
    }
}
