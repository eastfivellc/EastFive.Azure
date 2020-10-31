using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace EastFive.Azure.Auth
{
    public delegate Task<TResult> ResolveRedirectionDelegate<TResult>(Uri relUri,
            HttpRequestMessage request, Guid? accountIdMaybe,
            IDictionary<string, string> authParams);

    public interface IResolveRedirection
    {
        float Order { get; }

        Task<(Func<HttpResponseMessage, HttpResponseMessage>, Uri)> ResolveAbsoluteUrlAsync(Uri relUri,
            HttpRequestMessage request, Guid? accountIdMaybe, 
            IDictionary<string, string> authParams);
    }
}
