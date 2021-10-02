using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

using EastFive.Api;

namespace EastFive.Azure.Auth
{
    public delegate Task<TResult> ResolveRedirectionDelegate<TResult>(Uri relUri,
            HttpRequestMessage request, Guid? accountIdMaybe,
            IDictionary<string, string> authParams);

    public interface IResolveRedirection
    {
        float Order { get; }

        Task<(Func<IHttpResponse, IHttpResponse>, Uri)> ResolveAbsoluteUrlAsync(Uri relUri,
            IHttpRequest request, Guid? accountIdMaybe, 
            IDictionary<string, string> authParams);
    }
}
