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

        [Obsolete("User Login Providers from Application.")]
        internal static TResult GetLoginProvider<TResult>(string method,
            Func<IProvideLogin, TResult> onSuccess,
            Func<TResult> onCredintialSystemNotAvailable,
            Func<string, TResult> onFailure)
        {
            if (ServiceConfiguration.loginProviders.IsDefault())
                return onFailure("Authentication system not initialized.");
            
            if (!ServiceConfiguration.loginProviders.ContainsKey(method))
                return onCredintialSystemNotAvailable();

            var provider = ServiceConfiguration.loginProviders[method];
            return onSuccess(provider);
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

        internal TResult GetLoginProviders<TResult>(
            Func<IProvideLogin[], TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            if (ServiceConfiguration.loginProviders.IsDefault())
                return onFailure("Authentication system not initialized.");
            
            return onSuccess(ServiceConfiguration.loginProviders.SelectValues().ToArray());
        }

        internal TResult GetManagementProvider<TResult>(string method,
            Func<IProvideLoginManagement, TResult> onSuccess,
            Func<TResult> onCredintialSystemNotAvailable,
            Func<string, TResult> onFailure)
        {
            if (ServiceConfiguration.managementProviders.IsDefault())
                return onFailure("Authentication system not initialized.");

            if (!ServiceConfiguration.managementProviders.ContainsKey(method))
                return onCredintialSystemNotAvailable();

            var provider = ServiceConfiguration.managementProviders[method];
            return onSuccess(provider);
        }
        
        #endregion

        private Credentials credentialMappings;
        public Credentials Credentials
        {
            get
            {
                if (default(Credentials) == credentialMappings)
                    credentialMappings = new Credentials(this, this.DataContext);
                return credentialMappings;
            }
        }

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

        private Authorizations authorizations;
        public Authorizations Authorizations
        {
            get
            {
                if (default(Authorizations) == authorizations)
                    authorizations = new Authorizations(this, this.DataContext);
                return authorizations;
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

        private Roles roles;
        public Roles Roles
        {

            get
            {
                if (default(Roles) == roles)
                    roles = new Roles(this, DataContext);
                return roles;
            }
        }
        
        private LoginProviders loginProviders;
        public LoginProviders LoginProviders
        {

            get
            {
                if (loginProviders.IsDefault())
                    loginProviders = new LoginProviders(this, DataContext);
                return loginProviders;
            }
        }

        private Azure.Monitoring.Monitoring monitoring;
        public Azure.Monitoring.Monitoring Monitoring
        {

            get
            {
                if (default(Azure.Monitoring.Monitoring) == monitoring)
                    monitoring = new Azure.Monitoring.Monitoring(this, DataContext);
                return monitoring;
            }
        }

        #region Authorizations

        public delegate bool GetCredentialDelegate(CredentialValidationMethodTypes validationMethod, Uri provider, string userId, string userToken);
        
        #endregion
    }
}
