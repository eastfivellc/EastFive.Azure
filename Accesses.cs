﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;

using BlackBarLabs.Collections.Async;
using BlackBarLabs.Extensions;
using EastFive.Security.SessionServer.Persistence.Documents;

namespace EastFive.Security.SessionServer
{
    public class Accesses
    {
        private Context context;
        private Persistence.DataContext dataContext;

        internal Accesses(Context context, Persistence.DataContext dataContext)
        {
            this.dataContext = dataContext;
            this.context = context;
        }

        public async Task<TResult> CreateAsync<TResult>(Guid accountId,
                CredentialValidationMethodTypes method, IDictionary<string, string> paramSet,
            Func<TResult> onSuccess,
            Func<TResult> onFailure)
        {
            return await dataContext.Accesses.CreateAsync(accountId, method, paramSet,
                onSuccess,
                onFailure);
        }

        public async Task<TResult> FindByActorAsync<TResult>(Guid accountId,
                CredentialValidationMethodTypes method,
            Func<HttpClient, IDictionary<string, string>, TResult> onSessionCreated,
            Func<TResult> onAccessNotFound,
            Func<string, TResult> onFailure)
        {
            return await context.GetAccessProvider(method,
                async (accessProvider) =>
                {
                    return await await dataContext.Accesses.FindAsync(accountId, method,
                        paramSet =>
                        {
                            return accessProvider.CreateSessionAsync(paramSet,
                                (client, extraParams) =>
                                {
                                    return onSessionCreated(client, extraParams);
                                },
                                onFailure);
                        },
                        onAccessNotFound.AsAsyncFunc());
                },
                () => onFailure("").ToTask(),
                onFailure.AsAsyncFunc());
        }
        

    }
}
