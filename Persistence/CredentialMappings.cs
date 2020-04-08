using System;
using System.Text;
using System.Threading.Tasks;
using BlackBarLabs.Persistence.Azure;
using BlackBarLabs.Persistence.Azure.StorageTables;
using BlackBarLabs.Extensions;
using BlackBarLabs.Persistence;
using System.Linq;
using EastFive.Linq;
using System.Collections.Generic;
using EastFive.Api.Azure.Credentials;

namespace EastFive.Security.SessionServer.Persistence
{
    public class CredentialMappings
    {
        private AzureStorageRepository repository;
        public CredentialMappings(AzureStorageRepository repository)
        {
            this.repository = repository;
        }
        
        public async Task<TResult> LookupCredentialMappingAsync<TResult>(
                string method, string subject, Guid? loginId,
            Func<Guid, TResult> onSuccess,
            Func<TResult> onNotExist)
        {
            var partitionKey = method;
            return await await repository.FindLinkedDocumentAsync(subject, partitionKey,
                (Azure.Documents.CredentialMappingLookupDocument document) => document.CredentialMappingId,
                (Azure.Documents.CredentialMappingLookupDocument lookupDoc, Documents.CredentialMappingDocument mappingDoc) =>
                    onSuccess(mappingDoc.ActorId).ToTask(),
                async () => loginId.HasValue?
                    await await repository.FindByIdAsync(loginId.Value,
                        (Documents.LoginActorLookupDocument document) =>
                        {
                            // Migrate this by creating a CredentialMappingLookupDoc with the subject/partitionKey(method)
                            return CreateCredentialMappingAsync(Guid.NewGuid(), method, subject, document.ActorId,
                                () => onSuccess(document.ActorId),
                                () => onSuccess(document.ActorId),
                                () => onSuccess(document.ActorId));
                        },
                        onNotExist.AsAsyncFunc())
                    :
                    onNotExist(),
                (lookupDoc) => onNotExist().ToTask());
        }

        public async Task<TResult> LookupCredentialMappingByIdAsync<TResult>(
                Guid loginId,
            Func<Guid, TResult> onSuccess,
            Func<TResult> onNotExist)
        {
            return await repository.FindByIdAsync(loginId,
                (Documents.LoginActorLookupDocument document) => onSuccess(document.ActorId),
                () => onNotExist());
        }

        internal async Task<TResult> CreateCredentialMappingAsync<TResult>(Guid credentialMappingId,
                string method, string subjectId, Guid actorId,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists,
            Func<TResult> onTokenAlreadyInUse)
        {
            var rollback = new RollbackAsync<TResult>();
            
            var credentialMappingDoc = new Documents.CredentialMappingDocument()
            {
                Method = method,
                Subject = subjectId,
                ActorId = actorId,
            };
            rollback.AddTaskCreate(credentialMappingId, credentialMappingDoc, onAlreadyExists, this.repository);
            
            var lookupDocument = new Azure.Documents.CredentialMappingLookupDocument()
            {
                CredentialMappingId = credentialMappingId,
                Method = method,
                Subject = subjectId,
            };
            rollback.AddTaskCreate(subjectId, method, lookupDocument, onTokenAlreadyInUse, this.repository);
            
            rollback.AddTaskCreateOrUpdate(actorId,
                (Documents.ActorMappingsDocument authDoc) => authDoc.AddInviteId(credentialMappingId),
                (authDoc) => authDoc.RemoveInviteId(credentialMappingId),
                onAlreadyExists, // This should fail on the action above as well
                this.repository);

            return await rollback.ExecuteAsync(onSuccess);
        }

        [Obsolete("Credential Mappings are now mapped by method and subject")]
        public async Task<TResult> FindAllActorMappingsAsync<TResult>(
            Func<Guid[], TResult> onSuccess)
        {
            return await repository.FindAllAsync(
                (Documents.ActorMappingsDocument[] docs) =>
                    onSuccess(docs.Select(x => x.Id).ToArray())
                );
        }
        
        internal async Task<TResult> CreateCredentialMappingAsync<TResult>(Guid credentialMappingId,
            Guid loginId, Guid actorId, string email, Guid token, DateTime lastSent, bool isToken, bool overrideLoginId,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists,
            Func<TResult> onTokenAlreadyInUse,
            Func<TResult> onLoginAlreadyInUse)
        {
            var rollback = new RollbackAsync<TResult>();

            var inviteDocument = new Documents.InviteDocument()
            {
                ActorId = actorId,
                Email = email,
                IsToken = isToken,
                LastSent = lastSent,
                Token = token,
                LoginId = loginId,
            };
            rollback.AddTaskCreate(credentialMappingId, inviteDocument, onAlreadyExists, this.repository);

            var inviteTokenDocument = new Documents.InviteTokenDocument()
            {
                ActorId = actorId,
                InviteId = credentialMappingId,
            };
            rollback.AddTaskCreate(token, inviteTokenDocument, onTokenAlreadyInUse, this.repository);

            if (overrideLoginId)
            {
                var oldActorId = default(Guid);
                rollback.AddTaskCreateOrUpdate(loginId,
                    (Documents.LoginActorLookupDocument loginActorDocument) =>
                    {
                        oldActorId = loginActorDocument.ActorId;
                        loginActorDocument.ActorId = actorId;
                        return true;
                    },
                    (loginActorDocument) =>
                    {
                        loginActorDocument.ActorId = oldActorId;
                        return true;
                    },
                    () => { throw new Exception("Login override failed"); }, // Should never happend because always return true on mutate
                    this.repository);
            }
            else
            {
                var loginActorDocument = new Documents.LoginActorLookupDocument()
                {
                    ActorId = actorId,
                };
                rollback.AddTaskCreate(loginId, loginActorDocument, onLoginAlreadyInUse, this.repository);
            }
            
            rollback.AddTaskCreateOrUpdate(actorId,
                (Documents.ActorMappingsDocument authDoc) => authDoc.AddInviteId(credentialMappingId),
                (authDoc) => authDoc.RemoveInviteId(credentialMappingId),
                onAlreadyExists, // This should fail on the action above as well
                this.repository);

            return await rollback.ExecuteAsync(onSuccess);
        }
        
        internal async Task<TResult> DeleteInviteCredentialAsync<TResult>(Guid inviteId, 
            Func<Invite, Func<Task>, Task<TResult>> onFound, 
            Func<TResult> onNotFound)
        {
            return await this.repository.DeleteIfAsync<Documents.InviteDocument, TResult>(inviteId,
                (inviteDoc, deleteInviteAsync) =>
                {
                    var invite = Convert(inviteDoc);
                    return onFound(invite,
                        async () =>
                        {
                            var deletedInviteTask = this.repository.DeleteIfAsync<Documents.InviteTokenDocument, bool>(inviteDoc.Token,
                                async (inviteTokenDoc, deleteInviteTokenAsync) =>
                                {
                                    await deleteInviteTokenAsync();
                                    return true;
                                },
                                () => false);
                            var updatedActorMappingTask = this.repository.UpdateAsync<Documents.ActorMappingsDocument, bool>(inviteDoc.ActorId,
                                async (actorDoc, saveAsync) =>
                                {
                                    var r = actorDoc.RemoveInviteId(inviteId);
                                    await saveAsync(actorDoc);
                                    return r;
                                },
                                () => false);
                            await deleteInviteAsync();
                            await deletedInviteTask;
                            await updatedActorMappingTask;
                        });
                },
                () => onNotFound());
        }

        internal Task<TResult> UpdateTokenCredentialAsync<TResult>(Guid tokenCredentialId,
            Func<string, DateTime?, Guid, Func<string, DateTime?, Task>, Task<TResult>> onFound,
            Func<TResult> onNotFound)
        {
            return this.repository.UpdateAsync<Documents.InviteDocument, TResult>(tokenCredentialId,
                async (currentDoc, saveAsync) =>
                {
                    return await onFound(currentDoc.Email, currentDoc.LastSent, currentDoc.Token,
                        async (email, lastSent) =>
                        {
                            currentDoc.Email = email;
                            currentDoc.LastSent = lastSent;
                            await saveAsync(currentDoc);
                        });
                },
                () => onNotFound());
        }

        internal Task<TResult> FindInviteAsync<TResult>(Guid inviteId, bool isToken,
            Func<Guid, string, DateTime?, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return repository.FindByIdAsync(inviteId,
                (Documents.InviteDocument document) =>
                {
                    if (isToken != document.IsToken)
                        return onNotFound();
                    return onFound(document.ActorId, document.Email, document.LastSent);
                },
                () => onNotFound());
        }
        
        internal async Task<TResult> FindInviteByTokenAsync<TResult>(Guid token,
            Func<Guid, Guid, Guid?, TResult> onSuccess,
            Func<TResult> onNotFound)
        {
            return await await repository.FindByIdAsync(token,
                (Documents.InviteTokenDocument document) =>
                    repository.FindByIdAsync(document.InviteId,
                        (Documents.InviteDocument inviteDoc) =>
                        {
                            if (inviteDoc.IsToken)
                                return onNotFound();
                            return onSuccess(document.InviteId, inviteDoc.ActorId, inviteDoc.LoginId);
                        },
                        () =>
                        {
                            // TODO: Log data inconsistency
                            return onNotFound();
                        }),
                () => onNotFound().ToTask());
        }

        internal async Task<TResult> FindTokenCredentialByTokenAsync<TResult>(Guid token,
            Func<Guid, Guid, Guid?, TResult> onSuccess,
            Func<TResult> onNotFound)
        {
            return await await repository.FindByIdAsync(token,
                (Documents.InviteTokenDocument document) =>
                    repository.FindByIdAsync(document.InviteId,
                        (Documents.InviteDocument inviteDoc) =>
                        {
                            if (!inviteDoc.IsToken)
                                return onNotFound();
                            return onSuccess(document.InviteId, inviteDoc.ActorId, inviteDoc.LoginId);
                        },
                        () =>
                        {
                            // TODO: Log data inconsistency
                            return onNotFound();
                        }),
                () => onNotFound().ToTask());
        }

        internal async Task<TResult> FindInviteByActorAsync<TResult>(Guid actorId, bool isToken,
            Func<Invite[], TResult> onSuccess,
            Func<TResult> onNotFound)
        {
            return await repository.FindLinkedDocumentsAsync(actorId,
                (document) => document.GetInviteIds(),
                (Documents.ActorMappingsDocument authDoc, Documents.InviteDocument[] inviteDocs) =>
                {
                    var invites = inviteDocs.Where(doc => doc.IsToken == isToken).Select(Convert).ToArray();
                    return onSuccess(invites);
                },
                () => onNotFound());
        }
        
        private static Invite Convert(Documents.InviteDocument inviteDoc)
        {
            return new Invite
            {
                id = inviteDoc.Id,
                actorId = inviteDoc.ActorId,
                email = inviteDoc.Email,
                lastSent = inviteDoc.LastSent,
            };
        }

        private static CredentialMapping Convert(Documents.CredentialMappingDocument mappingDoc)
        {
            Guid.TryParse(mappingDoc.Subject, out Guid loginId);
            return new CredentialMapping
            {
                id = mappingDoc.Id,
                actorId = mappingDoc.ActorId,
                method =  mappingDoc.Method,
                subject = mappingDoc.Subject,
                loginId = loginId
            };
        }

        internal async Task<TResult> MarkInviteRedeemedAsync<TResult>(Guid inviteToken, Guid loginId,
            Func<Guid, TResult> onFound,
            Func<TResult> onNotFound,
            Func<Guid, TResult> onAlreadyRedeemed,
            Func<TResult> onAlreadyInUse,
            Func<TResult> onAlreadyConnected)
        {
            var lookupResults = await await repository.FindByIdAsync(inviteToken,
                async (Documents.InviteTokenDocument tokenDocument) =>
                {
                    var rollback = new RollbackAsync<TResult>();

                    rollback.AddTaskUpdate(tokenDocument.InviteId,
                        (Documents.InviteDocument inviteDoc) => { inviteDoc.LoginId = loginId; },
                        (inviteDoc) => { inviteDoc.LoginId = default(Guid?); },
                        onNotFound,
                        this.repository);

                    var loginLookup = new Documents.LoginActorLookupDocument()
                    {
                        ActorId = tokenDocument.ActorId,
                    };
                    rollback.AddTaskCreate(loginId, loginLookup, onAlreadyInUse, this.repository);

                    return await rollback.ExecuteAsync(() => onFound(tokenDocument.ActorId));
                },
                () => onNotFound().ToTask());
            return lookupResults;
        }
    }
}