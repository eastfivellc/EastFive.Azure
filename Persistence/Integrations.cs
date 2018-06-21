﻿using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BlackBarLabs.Collections.Async;
using BlackBarLabs.Persistence.Azure;
using BlackBarLabs.Persistence.Azure.StorageTables;
using BlackBarLabs.Extensions;
using BlackBarLabs.Persistence;
using BlackBarLabs.Persistence.Azure.Extensions;
using System.Linq;
using BlackBarLabs.Linq;
using BlackBarLabs;
using System.Collections.Generic;
using System.IdentityModel;
using System.Net.Http;
using EastFive.Security.SessionServer.Persistence.Documents;
using EastFive.Serialization;
using Microsoft.WindowsAzure.Storage.Table;
using EastFive.Collections.Generic;
using EastFive.Linq;

namespace EastFive.Security.SessionServer.Persistence
{
    public class Integrations
    {
        private AzureStorageRepository repository;

        public Integrations(AzureStorageRepository repository)
        {
            this.repository = repository;
        }

        public async Task<TResult> CreateUnauthenticatedAsync<TResult>(Guid integrationId, Guid accountId, 
                string methodName,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists)
        {
            var rollback = new RollbackAsync<TResult>();
            
            var docByMethod = new AccessDocument
            {
                LookupId = integrationId,
                Method = methodName,
            };
            rollback.AddTaskCreate(accountId, methodName, docByMethod, onAlreadyExists, this.repository);

            var docById = new AccessDocument
            {
                LookupId = accountId,
                Method = methodName,
            };
            rollback.AddTaskCreate(integrationId, docById, onAlreadyExists, this.repository);

            return await rollback.ExecuteAsync(onSuccess);
        }

        public async Task<TResult> CreateAuthenticatedAsync<TResult>(Guid integrationId, Guid authenticationId,
                string methodName, IDictionary<string, string> paramSet,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists)
        {
            var rollback = new RollbackAsync<TResult>();

            var docByMethod = new AccessDocument
            {
                LookupId = integrationId,
                Method = methodName,
            };
            rollback.AddTaskCreate(authenticationId, methodName, docByMethod, onAlreadyExists, this.repository);

            var integrationDoc = new AuthenticationRequestDocument
            {
                LinkedAuthenticationId = authenticationId,
                Method = methodName,
                Action = Enum.GetName(typeof(AuthenticationActions), AuthenticationActions.access)
            };
            integrationDoc.SetExtraParams(paramSet);
            rollback.AddTaskCreate(integrationId, integrationDoc, onAlreadyExists, this.repository);

            return await rollback.ExecuteAsync(onSuccess);
        }

        internal async Task<TResult> CreateOrUpdateAsync<TResult>(Guid actorId, string methodName,
            Func<Guid?, IDictionary<string, string>, Func<IDictionary<string, string>, Task<Guid>>, Task<TResult>> onFound)
        {
            return await await repository.FindLinkedDocumentAsync(actorId, methodName,
                accessDoc => accessDoc.LookupId,
                (AccessDocument accessDoc, AuthenticationRequestDocument authRequestDoc) =>
                {
                    return onFound(authRequestDoc.Id, authRequestDoc.GetExtraParams(),
                        async (updatedParams)=>
                        {
                            authRequestDoc.LinkedAuthenticationId = actorId;
                            authRequestDoc.SetExtraParams(updatedParams);
                            return await repository.UpdateIfNotModifiedAsync(authRequestDoc, ()=> accessDoc.LookupId, ()=> accessDoc.LookupId); 
                        });
                },
                ()=>
                {
                    return onFound(default(Guid?), default(IDictionary<string, string>),
                        async (parameters) =>
                        {
                            var integrationId = Guid.NewGuid();
                            return await CreateAuthenticatedAsync(integrationId, actorId, methodName, parameters,
                                ()=> integrationId,
                                "Guid not unique".AsFunctionException<Guid>());
                        });
                },
                async (parentDoc) =>
                {
                    await repository.DeleteAsync(parentDoc, ()=> true, ()=> false);
                    return await CreateOrUpdateAsync(actorId, methodName, onFound);
                });
        }

        public async Task<EastFive.Api.Azure.Integration[]> FindAsync(Guid actorId)
        {
            try
            {
                var query = new TableQuery<AccessDocument>().Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, actorId.AsRowKey()));
                var accessDocs = await repository.FindByQueryAsync(query);
                return await accessDocs
                    .FlatMap(
                        async (accessDoc, next, skip) => await await repository.FindByIdAsync(
                            accessDoc.LookupId,
                                (AuthenticationRequestDocument integrationDoc) =>
                                    next(
                                        new EastFive.Api.Azure.Integration
                                        {
                                            integrationId = integrationDoc.Id,
                                            method = integrationDoc.Method,
                                            parameters = integrationDoc.GetExtraParams(),
                                            authorizationId = actorId,
                                        }),
                        () => skip()),
                    (IEnumerable<EastFive.Api.Azure.Integration> integrations) =>
                        integrations.ToArray().ToTask());
            }
            catch(Exception ex)
            {
                return null;
            }
        }

        public Task<TResult> FindAuthorizedAsync<TResult>(Guid integrationId,
            Func<EastFive.Api.Azure.Integration, TResult> onSuccess,
            Func<TResult> onNotFoundOrUnauthorized)
        {
            return repository.FindByIdAsync(integrationId,
                (AuthenticationRequestDocument integrationDoc) =>
                {
                    if (!integrationDoc.LinkedAuthenticationId.HasValue)
                        return onNotFoundOrUnauthorized();
                    return onSuccess(
                        new EastFive.Api.Azure.Integration
                        {
                            integrationId = integrationDoc.Id,
                            method = integrationDoc.Method,
                            parameters = integrationDoc.GetExtraParams(),
                            authorizationId = integrationDoc.LinkedAuthenticationId.Value,
                        });
                },
                onNotFoundOrUnauthorized);
        }

        public async Task<TResult> FindAsync<TResult>(Guid actorId, string methodName,
            Func<Guid, TResult> found,
            Func<TResult> actorNotFound)
        {
            var results = await repository.FindByIdAsync(actorId, methodName,
                (AccessDocument doc) => found(doc.LookupId),
                () => actorNotFound());
            return results;
        }

        internal async Task<TResult> FindUpdatableAsync<TResult>(Guid actorId, string method,
            Func<Guid, IDictionary<string, string>, Func<IDictionary<string, string>, Task>, Task<TResult>> onFound,
            Func<Func<Guid, IDictionary<string, string>, Task<Guid>>, Task<TResult>> onNotFound)
        {
            var results = await await repository.UpdateAsync<AccessDocument, Task<TResult>>(actorId, method,
                (doc, saveAsync) =>
                {
                    return onFound(doc.LookupId, doc.GetExtraParams(),
                        (extraParamsNew) =>
                        {
                            doc.SetExtraParams(extraParamsNew);
                            return saveAsync(doc);
                        }).ToTask();
                },
                () =>
                {
                    return onNotFound(
                        (integrationId, extraParams) =>
                        {
                            return this.CreateUnauthenticatedAsync(integrationId, actorId, method, //extraParams,
                                () => integrationId,
                                "Guid not unique".AsFunctionException<Guid>());
                        });
                });
            return results;
        }

        public async Task<TResult> DeleteAsync<TResult>(Guid accessId,
            Func<CredentialValidationMethodTypes, IDictionary<string, string>, TResult> onDeleted,
            Func<TResult> actorNotFound)
        {
            var results = await repository.DeleteIfAsync<AccessDocument, TResult>(accessId,
                async (doc, deleteAsync) =>
                {
                    await deleteAsync();
                    Enum.TryParse(doc.Method, out CredentialValidationMethodTypes method);
                    return await repository.DeleteIfAsync<AccessDocument, TResult>(doc.LookupId, doc.Method,
                        async (lookupDoc, deleteLookupAsync) =>
                        {
                            await deleteLookupAsync();
                            return onDeleted(method, doc.GetExtraParams());
                        },
                        () => onDeleted(method, doc.GetExtraParams()));
                },
                () => actorNotFound());
            return results;
        }

        public async Task<TResult> DeleteAsync<TResult>(Guid actorId, string method,
            Func<IDictionary<string, string>, TResult> onDeleted,
            Func<TResult> actorNotFound)
        {
            var results = await repository.DeleteIfAsync<AccessDocument, TResult>(actorId, method,
                async (doc, deleteAsync) =>
                {
                    await deleteAsync();
                    return await repository.DeleteIfAsync<AccessDocument, TResult>(doc.LookupId,
                        async (lookupDoc, deleteLookupAsync) =>
                        {
                            await deleteLookupAsync();
                            return onDeleted(doc.GetExtraParams());
                        },
                        () => onDeleted(doc.GetExtraParams()));
                },
                () => actorNotFound());
            return results;
        }
    }
}