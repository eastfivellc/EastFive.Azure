//using System;
//using System.Security.Cryptography;
//using System.Text;
//using System.Threading.Tasks;
//using System.Linq;
//using System.Collections.Generic;
//using System.Net.Http;

//using BlackBarLabs.Persistence.Azure;
//using BlackBarLabs.Persistence.Azure.StorageTables;
//using BlackBarLabs.Extensions;
//using BlackBarLabs.Persistence;
//using EastFive.Security.SessionServer.Persistence.Documents;
//using EastFive.Serialization;

//namespace EastFive.Security.SessionServer.Persistence
//{
//    public class Claims
//    {
//        private AzureStorageRepository repository;

//        public Claims(AzureStorageRepository repository)
//        {
//            this.repository = repository;
//        }

//        public async Task<TResult> CreateOrUpdateAsync<TResult>(Guid actorId, Guid claimId, string type, string value,
//            Func<TResult> onSuccess,
//            Func<TResult> onFailure,
//            Func<TResult> onActorNotFound)
//        {
//            return await await repository.FindLinkedDocumentsAsync<ActorMappingsDocument, ClaimDocument, Task<TResult>>(actorId,
//                (ActorMappingsDocument actorMappingsDocument) => actorMappingsDocument.Claims.ToGuidsFromByteArray(),
//                async (ActorMappingsDocument actorMappingsDocument, ClaimDocument[] claimDocuments) =>
//                {
//                    var claimDocs =
//                        claimDocuments.Where(
//                            doc => string.Compare(doc.Type, type, StringComparison.OrdinalIgnoreCase) == 0)
//                            .ToArray();
//                    if (claimDocs.Length >= 1)
//                    {
//                        var claimDoc = claimDocs[0];
//                        return await repository.UpdateAsync<ClaimDocument, TResult>(claimDoc.ClaimId,
//                            async (currentDoc, saveAsync) =>
//                            {
//                                currentDoc.Value = value;
//                                await saveAsync(currentDoc);
//                                return onSuccess();
//                            },
//                            ()=> onFailure());
//                    }

//                    var rollback = new RollbackAsync<TResult>();
//                    var newClaimDoc = new ClaimDocument()
//                    {
//                        ClaimId = claimId,
//                        Issuer = actorId.ToString("N"), //TODO - Is this is the correct issuer data???
//                        Type = type,
//                        Value = value
//                    };
//                    rollback.AddTaskCreate(claimId, newClaimDoc, onFailure, repository);
//                    rollback.AddTaskUpdate(actorId,
//                        (ActorMappingsDocument actorMapDocument) => actorMapDocument.AddClaim(claimId),
//                        (actorMapDocument) => actorMapDocument.RemoveClaim(claimId),
//                        onActorNotFound, repository);
//                    return await rollback.ExecuteAsync(onSuccess);
//                },
//                ()=> onActorNotFound().ToTask());
//        }

//        public async Task<TResult> FindAsync<TResult>(Guid actorId,
//            Func<Claim[], TResult> found,
//            Func<TResult> actorNotFound)
//        {
//            var results = await repository.FindLinkedDocumentsAsync(actorId,
//                (ActorMappingsDocument actorMappingsDocument) =>
//                {
//                    var claims = actorMappingsDocument.Claims.ToGuidsFromByteArray();
//                    return claims;
//                },
//                (ActorMappingsDocument document, ClaimDocument [] claims) => found(claims.Select(claim =>
//                    new Claim
//                    {
//                        claimId = claim.ClaimId,
//                        issuer = claim.Issuer,
//                        type = claim.Type,
//                        value = claim.Value,
//                    }).ToArray()),
//                () => actorNotFound());
//            return results;
//        }
//    }
//}