using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Auth
{
    public interface IProvideLoginProvider
    {
        Task<TResult> ProvideLoginProviderAsync<TResult>(
            Func<IProvideLogin, TResult> onLoaded,
            Func<string, TResult> onNotAvailable);
    }
}
