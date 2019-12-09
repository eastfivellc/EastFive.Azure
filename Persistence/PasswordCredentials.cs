using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BlackBarLabs.Persistence.Azure;
using BlackBarLabs.Persistence.Azure.StorageTables;
using BlackBarLabs.Extensions;
using BlackBarLabs.Persistence;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;

namespace EastFive.Security.SessionServer.Persistence
{
    public struct CredentialMapping
    {
        public Guid id;
        public Guid actorId;
        public Guid loginId;
        public DateTime? lastSent;
        public string method;
        public string subject;
    }

    public class PasswordCredentials
    {
        private AzureStorageRepository repository;
        private DataContext context;

        public PasswordCredentials(DataContext context, AzureStorageRepository repository)
        {
            this.repository = repository;
            this.context = context;
        }

        internal async Task<TResult> UpdatePasswordCredentialAsync<TResult>(Guid passwordCredentialId,
            Func<Guid, Guid, DateTime?, Func<DateTime, Task>, Task<TResult>> onFound,
            Func<TResult> onNotFound)
        {
            return await this.repository.UpdateAsync<Documents.PasswordCredentialDocument, TResult>(passwordCredentialId,
                async (doc, saveAsync) =>
                {
                    return await await this.repository.FindByIdAsync(doc.LoginId,
                        (Documents.LoginActorLookupDocument lookupDoc) =>
                        {
                            return onFound(lookupDoc.ActorId, doc.LoginId, doc.EmailLastSent,
                                async (emailLastSentUpdated) =>
                                {
                                    doc.EmailLastSent = emailLastSentUpdated;
                                    await saveAsync(doc);
                                });
                        },
                        () => onNotFound().ToTask()); // TODO: Log data corruption here
                },
                () => onNotFound());
        }

        public async Task<TResult> CreatePasswordCredentialAsync<TResult>(Guid passwordCredentialId,
            Guid actorId, Guid loginId, DateTime? emailLastSent,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists,
            Func<TResult> onRelationshipAlreadyExists,
            Func<TResult> onLoginAlreadyUsed)
        {
            var rollback = new RollbackAsync<TResult>();

            var lookupDoc = new Documents.LoginActorLookupDocument
            {
                ActorId = actorId,
            };
            rollback.AddTaskCreate(loginId, lookupDoc,
                onLoginAlreadyUsed, this.repository);

            rollback.AddTaskCreateOrUpdate(actorId,
                (Documents.ActorMappingsDocument actorDoc) =>
                    actorDoc.AddPasswordCredential(passwordCredentialId),
                actorDoc => actorDoc.RemovePasswordCredential(passwordCredentialId),
                onRelationshipAlreadyExists,
                this.repository);

            var passwordCredentialDoc = new Documents.PasswordCredentialDocument
            {
                LoginId = loginId,
                EmailLastSent = emailLastSent,
            };
            rollback.AddTaskCreate(passwordCredentialId, passwordCredentialDoc,
                    onAlreadyExists, this.repository);

            return await rollback.ExecuteAsync(onSuccess);
        }

        public async Task<TResult> DeletePasswordCredentialAsync<TResult>(Guid passwordCredentialId,
            Func<Guid, TResult> onSuccess,
            Func<TResult> onNotFound)
        {
            return await this.repository.DeleteIfAsync<Documents.PasswordCredentialDocument, TResult>(passwordCredentialId,
                async (pcDoc, deletePCDocAsync) =>
                {
                    await deletePCDocAsync();
                    return await this.repository.DeleteIfAsync<Documents.LoginActorLookupDocument, TResult>(pcDoc.LoginId,
                        async (lookupDoc, deleteLookupDocAsync) =>
                        {
                            await deleteLookupDocAsync();
                            return await this.repository.UpdateAsync<Documents.ActorMappingsDocument, TResult>(lookupDoc.ActorId,
                                async (actorDoc, saveActorDocAsync) =>
                                {
                                    actorDoc.RemovePasswordCredential(passwordCredentialId);
                                    await saveActorDocAsync(actorDoc);
                                    return onSuccess(pcDoc.LoginId);
                                },
                                () => onSuccess(pcDoc.LoginId));
                        },
                        onNotFound);
                },
                onNotFound);
        }

        public async Task<TResult> FindPasswordCredentialAsync<TResult>(Guid passwordCredentialId,
            Func<Guid, Guid, DateTime?, TResult> onSuccess,
            Func<TResult> onNotFound)
        {
            return await await repository.FindByIdAsync(passwordCredentialId,
                (Documents.PasswordCredentialDocument document) =>
                    context.CredentialMappings.LookupCredentialMappingByIdAsync(
                            document.LoginId,
                        (actorId) => onSuccess(actorId, document.LoginId, document.EmailLastSent),
                        () => onNotFound()),
                () => onNotFound().ToTask());
        }

        public async Task<TResult> FindPasswordCredentialByActorAsync<TResult>(Guid actorId,
            Func<CredentialMapping[], TResult> onSuccess,
            Func<TResult> onNotFound)
        {
            return await repository.FindLinkedDocumentsAsync(actorId,
                (document) => document.GetPasswordCredentials(),
                (Documents.ActorMappingsDocument authDoc, Documents.PasswordCredentialDocument[] passwordCredentialDocs) =>
                {
                    var invites = passwordCredentialDocs.Select(pcDoc => new CredentialMapping
                    {
                        id = pcDoc.Id,
                        actorId = actorId,
                        loginId = pcDoc.LoginId,
                        lastSent = pcDoc.EmailLastSent,
                    }).ToArray();
                    return onSuccess(invites);
                },
                () => onNotFound());
        }

        public async Task<TResult> FindPasswordCredentialByLoginIdAsync<TResult>(Guid loginId,
            Func<CredentialMapping, TResult> onSuccess,
            Func<TResult> onNotFound)
        {
            return await await repository.FindByIdAsync(loginId,
                (Documents.LoginActorLookupDocument loginDoc) =>
                {
                    return this.FindPasswordCredentialByActorAsync(loginDoc.ActorId,
                        (mappings) =>
                        {
                            var matches = mappings.Where(mapping => mapping.loginId == loginId).ToArray();
                            if (matches.Length == 0)
                                return onNotFound();
                            var match = matches.First();
                            return onSuccess(match);
                        },
                        onNotFound);
                },
                () => onNotFound().ToTask());
        }

        public struct PasswordCredentialInfo
        {
            public Guid Id { get; set; }
            public Guid LoginId { get; set; }
            public DateTime? EmailLastSent { get; set; }
        }

        public async Task<TResult> FindAllAsync<TResult>(
            Func<PasswordCredentialInfo[], TResult> success)
        {
            var results = await this.repository.FindAllAsync<Documents.PasswordCredentialDocument, TResult>(
                (passwordCredentialDocs) =>
                {
                    var passwordCredentialInfos = passwordCredentialDocs
                        .Select(
                            (Documents.PasswordCredentialDocument passwordCredentialDoc) =>
                                new PasswordCredentialInfo
                                {
                                    Id = passwordCredentialDoc.Id,
                                    LoginId = passwordCredentialDoc.LoginId,
                                    EmailLastSent = passwordCredentialDoc.EmailLastSent,
                                })
                        .ToArray();
                    return success(passwordCredentialInfos);
                });
            return results;

            //var passwordCredentialDocs = this.repository.FindAllAsync<Documents.PasswordCredentialDocument>();
            //var passwordCredentialInfos = passwordCredentialDocs
            //    .ToEnumerable(
            //        (Documents.PasswordCredentialDocument passwordCredentialDoc) =>
            //            new PasswordCredentialInfo
            //            {
            //                Id = passwordCredentialDoc.Id,
            //                LoginId = passwordCredentialDoc.LoginId,
            //                EmailLastSent = passwordCredentialDoc.EmailLastSent
            //            })
            //    .ToArray();
            //return success(passwordCredentialInfos);
        }
    }
}