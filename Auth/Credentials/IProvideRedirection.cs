using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Azure.Auth;
using EastFive.Security.SessionServer;

namespace EastFive.Azure.Auth.CredentialProviders
{
    public interface IProvideRedirection
    {
        Task<TResult> GetRedirectUriAsync<TResult>(
                Guid? accountIdMaybe, IProvideAuthorization authProvider, IDictionary<string, string> authParams,
                EastFive.Azure.Auth.Method method, EastFive.Azure.Auth.Authorization authorization,
                EastFive.Api.Azure.AzureApplication application, IHttpRequest request,
                IInvokeApplication endpoints, Uri baseUrl,
            Func<Uri, KeyValuePair<string,string>[], TResult> onSuccess,
            Func<TResult> onIgnored,
            Func<string, string, TResult> onInvalidParameter,
            Func<string, TResult> onFailure);
    }
}
