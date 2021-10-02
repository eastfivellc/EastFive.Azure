using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
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
    }
}
