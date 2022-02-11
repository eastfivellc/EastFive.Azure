using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

using EastFive;
using EastFive.Api;
using EastFive.Azure;
using EastFive.Azure.Auth;

namespace EastFive.Azure.Auth
{
    public interface IProvideAccountInformation
    {
        //Task<TResult> CreateAccount<TResult>(
        //        string subject, IDictionary<string, string> extraParameters,
        //        Method authentication, Authorization authorization, Uri baseUri,
        //        IApiApplication webApiApplication,
        //    Func<Guid, TResult> onCreatedMapping,
        //    Func<TResult> onAllowSelfServeAccounts,
        //    Func<Uri, TResult> onInterceptProcess,
        //    Func<TResult> onNoChange);

        Task<TResult> FindOrCreateAccountByMethodAndKeyAsync<TResult>(
                Method authenticationMethod, string externalAccountKey,
                Authorization authorization, IDictionary<string, string> extraParams,
                IProvideLogin loginProvider, IHttpRequest request,
                Func<IAccount, IAccount> populateAccount,
            Func<Guid, IDictionary<string, string>, TResult> onAccountReady,
            Func<Uri, Guid, IDictionary<string, string>, TResult> onInterceptProcess,
            Func<string, TResult> onReject);

        //Task<TResult> CreateUnpopulatedAccountAsync<TResult>(
        //        Method authenticationMethod, string externalAccountKey,
        //        IAuthApplication application,
        //    Func<IAccount, Func<IAccount, Task>, Task<TResult>> onNeedsPopulated,
        //    Func<Uri, TResult> onInterupted,
        //    Func<string, TResult> onNotCreated,
        //    Func<TResult> onNoEffect);
    }

    public interface IProvideClaims
    {
        bool TryGetStandardClaimValue(string claimType, IDictionary<string, string> parameters, out string claimValue);
    }
}
