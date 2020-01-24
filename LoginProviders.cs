using System;
using System.Linq;
using System.Net.Http;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;

using BlackBarLabs.Extensions;
using BlackBarLabs.Api;
using BlackBarLabs.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using EastFive.Collections.Generic;
using EastFive.Extensions;

namespace EastFive.Security.SessionServer
{
    public struct LoginProvider
    {
        public Guid id;
        internal Uri loginUrl;
        internal Uri signupUrl;
        internal Uri logoutUrl;
    }

    public class LoginProviders
    {
        private Context context;
        private Persistence.DataContext dataContext;

        internal LoginProviders(Context context, Persistence.DataContext dataContext)
        {
            this.dataContext = dataContext;
            this.context = context;
        }

        public Task<TResult> GetAllAsync<TResult>(
            Func<KeyValuePair<string, IProvideLogin>[], TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            if (ServiceConfiguration.loginProviders.IsDefaultOrNull())
                return onFailure("System not initialized").ToTask();
            return onSuccess(ServiceConfiguration.loginProviders.ToArray()).ToTask();
        }

        public Task<TResult> GetAllAsync<TResult>(bool integrationOnly,
            Func<KeyValuePair<string,IProvideLogin>[], TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            if (!integrationOnly)
                return GetAllAsync(onSuccess, onFailure);
            return onSuccess(ServiceConfiguration.loginProviders
                .Where(x => x.Value is IProvideIntegration)
                .ToArray()
            ).ToTask();
        }
    }
}
