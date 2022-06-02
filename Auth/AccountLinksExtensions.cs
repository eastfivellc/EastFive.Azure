using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive;
using EastFive.Extensions;
using EastFive.Reflection;
using EastFive.Linq;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Collections.Generic;
using EastFive.Persistence;
using System.Threading.Tasks;
using EastFive.Api.Azure;
using EastFive.Azure.Auth.CredentialProviders;
using EastFive.Linq.Async;

namespace EastFive.Azure.Auth
{
    public static class AccountLinksExtensions
    {
        public static AccountLinks AppendCredentials(this AccountLinks accountLinks,
            Method authMethod, string accountKey) =>
                accountLinks.AddOrUpdateCredentials(authMethod.authenticationId, accountKey);

        public static AccountLinks AddOrUpdateCredentials(this AccountLinks accountLinks,
            IRef<Method> authMethodRef, string accountKey)
        {
            accountLinks.accountLinks = accountLinks.accountLinks
                .NullToEmpty()
                .Where(al => al.method.id != authMethodRef.id)
                .Append(
                    new AccountLink
                    {
                        method = authMethodRef,
                        externalAccountKey = accountKey,
                    })
                .ToArray();
            return accountLinks;
        }

        public static AccountLinks AddOrUpdateCredentials(this AccountLinks accountLinks,
            Method authMethod, string accountKey, out bool modified)
        {
            AccountLinks alsUpdated;
            (alsUpdated, modified) = accountLinks.accountLinks
                .NullToEmpty()
                .Where(al => al.method.id == authMethod.authenticationId.id)
                .First(
                    (alMatch, next) =>
                    {
                        if (alMatch.externalAccountKey.Equals(accountKey))
                            return (accountLinks, false);

                        return next();
                    },
                    () =>
                    {
                        accountLinks.accountLinks = accountLinks.accountLinks
                            .NullToEmpty()
                            .Where(al => al.method.id != authMethod.authenticationId.id)
                            .Append(
                                new AccountLink
                                {
                                    method = authMethod.authenticationId,
                                    externalAccountKey = accountKey,
                                })
                            .ToArray();
                        return (accountLinks, true);
                    });
            return alsUpdated;
        }
        
        public static IEnumerableAsync<Method> DeleteInCredentialProviders(this AccountLinks accountLinks, IAuthApplication application)
        {
            var loginProvidersWithMethods = application.LoginProviders
                .SelectValues()
                .Select(
                    loginProvider =>
                    {
                        var method = Method.ByMethodName(loginProvider.Method, application);
                        return (method, loginProvider);
                    });

            var (accountLinkMethodLoginProviderKvps, unmatched1, unmatched2) = accountLinks.accountLinks
                .Match(loginProvidersWithMethods,
                    (accountLink, methodLoginProvider) => accountLink.method.id == methodLoginProvider.method.id);

            return accountLinkMethodLoginProviderKvps
                .Where(tpl => tpl.Item2.loginProvider is IProvideLoginManagement)
                .Select(
                    async accountLinkLoginProvider =>
                    {
                        var (accountLink, (method, loginProvider)) = accountLinkLoginProvider;
                        var loginManager = loginProvider as IProvideLoginManagement;
                        return await loginManager.DeleteAuthorizationAsync(accountLink.externalAccountKey,
                            () => method,
                            why => default(Method?),
                            () => default,
                            failure => default);
                    })
                .AsyncEnumerable()
                .SelectWhereHasValue();
        }
    }
}

