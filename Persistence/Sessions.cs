//using System;
//using System.Threading.Tasks;

//using BlackBarLabs.Persistence.Azure.StorageTables;

//namespace EastFive.Security.SessionServer.Persistence
//{
//    public class Sessions
//    {
//        private AzureStorageRepository repository;
//        public Sessions(AzureStorageRepository repository)
//        {
//            this.repository = repository;
//        }

//        #region Actionables

//        public async Task<TResult> CreateAsync<TResult>(Guid sessionId, string refreshToken, Guid authorizationId,
//            Func<TResult> success,
//            Func<TResult> alreadyExists)
//        {
//            var document = new Documents.SessionDocument()
//            {
//                SessionId = sessionId,
//                RefreshToken = refreshToken,
//                AuthorizationId = authorizationId,
//            };
//            return await this.repository.CreateAsync(sessionId, document,
//                () => success(),
//                () => alreadyExists());
//        }

//        public async Task<TResult> CreateAsync<TResult>(Guid sessionId, string refreshToken,
//            Func<TResult> success,
//            Func<TResult> alreadyExists)
//        {
//            var document = new Documents.SessionDocument()
//            {
//                SessionId = sessionId,
//                RefreshToken = refreshToken,
//            };
//            return await this.repository.CreateAsync(sessionId, document,
//                () => success(),
//                () => alreadyExists());
//        }

//        #endregion
        
//        public async Task<TResult> UpdateAuthentication<TResult>(Guid sessionId,
//            Func<Guid, Guid> authIdFunc,
//            Func<TResult> onSuccess,
//            Func<TResult> onNotFound)
//        {
//            var r = await repository.UpdateAsync<Documents.SessionDocument, TResult>(sessionId,
//                async (sessionDoc, save) =>
//                {
//                    sessionDoc.AuthorizationId = authIdFunc.Invoke(sessionDoc.AuthorizationId);
//                    await save(sessionDoc);
//                    return onSuccess();
//                },
//                () => onNotFound());
//            return r;
//        }

//        /// <summary>
//        /// Task&ltTResult&gt found(AuthorizationId, saveAuthId(updatedAuthId))
//        /// </summary>
//        /// <example>
//        /// UpdateAuthentication<T>(sessionId,
//        ///     async(authId, saveAuthId) =>
//        ///     {
//        ///         await saveAuthId(authorizationId);
//        ///         await true.ToTask(),
//        ///     }
//        ///     () => onNotFound("Error updating authentication"));
//        /// </example>
//        /// <typeparam name="TResult"></typeparam>
//        /// <param name="sessionId"></param>
//        /// <param name="found"></param>
//        /// <param name="notFound"></param>
//        /// <returns></returns>
//        public async Task<TResult> UpdateRefreshTokenAsync<TResult>(Guid sessionId,
//            Func<Guid, Func<Guid, Task>, Task<TResult>> found,
//            Func<TResult> notFound)
//        {
//            var result = await repository.UpdateAsync<Documents.SessionDocument, TResult>(sessionId,
//                async (sessionDoc, onSave) =>
//                {
//                    return await found(sessionDoc.AuthorizationId,
//                        async (updatedAuthId) =>
//                        {
//                            sessionDoc.AuthorizationId = updatedAuthId;
//                            await onSave(sessionDoc);
//                        });
//                },
//                () => notFound());
//            return result;
//        }
//    }
//}
