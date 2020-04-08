using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;

using BlackBarLabs.Persistence.Azure;
using EastFive.Security.CredentialProvider;
using EastFive.Api.Services;
using BlackBarLabs;
using BlackBarLabs.Extensions;
using BlackBarLabs.Linq;
using BlackBarLabs.Collections.Generic;
using BlackBarLabs.Api;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Serialization;
using EastFive.Api.Azure.Credentials;
using EastFive.Azure.Auth;
using EastFive.Azure.Auth.CredentialProviders;

namespace EastFive.Security.SessionServer
{
    public class Context
    {
        private Security.SessionServer.Persistence.DataContext dataContext;
        private readonly Func<Security.SessionServer.Persistence.DataContext> dataContextCreateFunc;
        
        public Context(Func<Security.SessionServer.Persistence.DataContext> dataContextCreateFunc)
        {
            this.dataContextCreateFunc = dataContextCreateFunc;
        }

        public static Context LoadFromConfiguration()
        {
            var context = new EastFive.Security.SessionServer.Context(
                () => new EastFive.Security.SessionServer.Persistence.DataContext(EastFive.Azure.AppSettings.ASTConnectionStringKey));
            return context;
        }

        public Security.SessionServer.Persistence.DataContext DataContext
        {
            get { return dataContext ?? (dataContext = dataContextCreateFunc.Invoke()); }
        }

        #region Services
        
        public async Task<TResult> CreateOrUpdateClaim<TResult>(Guid accountId, string claimType, string claimValue,
            Func<TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            var claimId = (accountId + claimType).MD5HashGuid();
            return await this.Claims.CreateOrUpdateAsync(accountId, claimId, claimType, claimValue,
                onSuccess,
                () => onFailure("Account was not found"),
                () => onFailure("Claim is already in use"));
        }


        //internal TResult GetAccessProvider<TResult>(CredentialValidationMethodTypes method,
        //    Func<IProvideAccess, TResult> onSuccess,
        //    Func<TResult> onCredintialSystemNotAvailable,
        //    Func<string, TResult> onFailure)
        //{
        //    if (ServiceConfiguration.accessProviders.IsDefault())
        //        return onFailure("Authentication system not initialized.");

        //    if (!ServiceConfiguration.accessProviders.ContainsKey(method))
        //        return onCredintialSystemNotAvailable();

        //    var provider = ServiceConfiguration.accessProviders[method];
        //    return onSuccess(provider);
        //}

        //internal TResult GetAccessProviders<TResult>(
        //    Func<IProvideAccess[], TResult> onSuccess,
        //    Func<string, TResult> onFailure)
        //{
        //    return onSuccess(ServiceConfiguration.accessProviders.SelectValues().ToArray());
        //}

        
        #endregion

        private Sessions sessions;
        public Sessions Sessions
        {
            get
            {
                if (default(Sessions) == sessions)
                    sessions = new Sessions(this, this.DataContext);
                return sessions;
            }
        }


        private EastFive.Azure.Integrations integrations;
        public EastFive.Azure.Integrations Integrations
        {
            get
            {
                if (default(EastFive.Azure.Integrations) == integrations)
                    integrations = new EastFive.Azure.Integrations(this, this.DataContext);
                return integrations;
            }
        }

        public Credentials invites;
        public Credentials Invites
        {
            get
            {
                if (default(Credentials) == invites)
                    invites = new Credentials(this, this.DataContext);
                return invites;
            }
        }

        private Claims claims;
        public Claims Claims
        {
            get
            {
                if (default(Claims) == claims)
                    claims = new Claims(this, this.DataContext);
                return claims;
            }
        }


        #region Authorizations

        #endregion
    }
}
