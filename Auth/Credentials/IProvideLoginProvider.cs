using System;
using System.Collections.Generic;
using System.Text;

namespace EastFive.Azure.Auth
{
    public interface IProvideLoginProvider
    {
        TResult ProvideLoginProvider<TResult>(
            Func<IProvideLogin, TResult> onLoaded,
            Func<string, TResult> onNotAvailable);
    }
}
