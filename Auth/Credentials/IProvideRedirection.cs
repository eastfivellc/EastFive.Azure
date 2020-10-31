using EastFive.Security.SessionServer;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EastFive.Api.Azure.Credentials
{
    public interface IProvideRedirection
    {
        Task<TResult> GetRedirectUriAsync<TResult>(
                Guid? accountIdMaybe, IProvideAuthorization authProvider, IDictionary<string, string> authParams,
                EastFive.Azure.Auth.Method method, EastFive.Azure.Auth.Authorization authorization,
                EastFive.Api.Azure.AzureApplication application, HttpRequestMessage request,
                IInvokeApplication endpoints, Uri baseUrl,
            Func<Uri, TResult> onSuccess,
            Func<TResult> onIgnored,
            Func<string, string, TResult> onInvalidParameter,
            Func<string, TResult> onFailure);
    }
}
