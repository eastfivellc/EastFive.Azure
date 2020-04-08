using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using EastFive.Api.Azure;
using EastFive.Api.Azure.Credentials;
using EastFive.Azure.Auth;
using EastFive.Extensions;
using EastFive.Security.SessionServer;

namespace EastFive.Azure.Auth.CredentialProviders
{
    class AzureStorageTablesLogAuthorizationRequestManager : IManageAuthorizationRequests
    {
        public async Task<TResult> CredentialValidation<TResult>(Guid requestId, 
            AzureApplication application, IRef<Method> method,
            IDictionary<string, string> values, 
            Func<Task<TResult>> onContinue, 
            Func<string, TResult> onStop)
        {
            var doc = new CredentialProcessDocument()
            {
                Method = method.id.ToString(),
                Message = "CREDENTIAL VALIDATION REQUESTED",
                Time = DateTime.UtcNow.Ticks,
            };
            doc.SetValuesRedirect(values);
            await CredentialProcessDocument.CreateAsync(requestId, doc, application.AzureContext.DataContext.AzureStorageRepository);
            var resp = await onContinue();
            //await loggingTask;  //TODO - We were storing the CreateAsync as this task and awaiting it later, but we ran into a race condition that this solves.  
            return resp;
        }

        public async Task<TResult> CreatedAuthenticationLoginAsync<TResult>(Guid requestId, 
                AzureApplication application,
                Guid sessionId, Guid authorizationId, 
                string token, string refreshToken,
                string method, AuthenticationActions action, IProvideAuthorization provider,
                IDictionary<string, string> extraParams, Uri redirectUrl,
            Func<Task<TResult>> onContinue,
            Func<string, TResult> onStop)
        {
            var loggingTask = CredentialProcessDocument.UpdateAsync(requestId,
                async (doc, saveAsync) =>
                {
                    doc.Message = "AUTHENTICATED";
                    doc.SessionId = sessionId;
                    doc.AuthorizationId = authorizationId;
                    doc.Token = token;
                    doc.RefreshToken = refreshToken;
                    doc.Action = Enum.GetName(typeof(AuthenticationActions), action);
                    doc.Provider = provider.GetType().FullName;
                    doc.SetValuesCredential(extraParams);
                    doc.RedirectUrl = redirectUrl.IsDefaultOrNull() ? string.Empty : redirectUrl.ToString();
                    await saveAsync(doc);
                    return true;
                },
                application.AzureContext.DataContext.AzureStorageRepository);
            var resp = await onContinue();
            bool b = await loggingTask;
            return resp;
        }

        public async Task<TResult> CreatedAuthenticationLogoutAsync<TResult>(Guid requestId, 
                AzureApplication application,
                string reason, string method,
                IProvideAuthorization provider, IDictionary<string, string> extraParams, Uri redirectUrl,
            Func<Task<TResult>> onContinue,
            Func<string, TResult> onStop)
        {
            var loggingTask = CredentialProcessDocument.UpdateAsync(requestId,
                async (doc, saveAsync) =>
                {
                    doc.Message = "LOGOUT";
                    doc.Action = Enum.GetName(typeof(AuthenticationActions), AuthenticationActions.signin);
                    doc.Provider = provider.GetType().FullName;
                    doc.SetValuesCredential(extraParams);
                    doc.RedirectUrl = redirectUrl.IsDefaultOrNull() ? string.Empty : redirectUrl.ToString();
                    await saveAsync(doc);
                    return true;
                },
                application.AzureContext.DataContext.AzureStorageRepository);
            var resp = await onContinue();
            bool b = await loggingTask;
            return resp;
        }
        
        public Task<TResult> CredentialUnmappedAsync<TResult>(Guid requestId, 
                AzureApplication application,
                string subject, string method, 
                IProvideAuthorization credentialProvider, IDictionary<string, string> extraParams,
                Func<Guid,
                    Func<Guid, string, string, AuthenticationActions, Uri, Task<Task<TResult>>>, 
                    Func<string, Task<TResult>>,
                    Task<Task<TResult>>> createMappingAsync, 
            Func<
                Func<Guid,
                    Func<Guid, string, string, AuthenticationActions, Uri, Task<Task<TResult>>>,
                    Func<string, Task<TResult>>,
                    Task<Task<TResult>>>,
                Task<TResult>> onContinue,
            Func<string, TResult> onStop)
        {
            return CredentialProcessDocument.UpdateAsync(requestId,
                async (doc, saveAsync) =>
                {
                    doc.Message = "CREDENTIAL LOOKUP NOT FOUND";
                    doc.Provider = credentialProvider.GetType().FullName;
                    doc.SetValuesCredential(extraParams);
                    await saveAsync(doc);
                    Func<Guid,
                        Func<Guid, string, string, AuthenticationActions, Uri, Task<Task<TResult>>>,
                        Func<string, Task<TResult>>,
                        Task<Task<TResult>>> createMappingWrappedAsync =
                            async (authorizationId, onCreated, onFailure) =>
                            {
                                doc.AuthorizationId = authorizationId;
                                Func<Guid, string, string, AuthenticationActions, Uri, Task<Task<TResult>>> onCreateWrapped =
                                    async (sessionId, token, refreshToken, action, redirectUrl) =>
                                    {
                                        doc.Message = "LOOKUP CREATED";
                                        doc.SessionId = sessionId;
                                        doc.Token = token;
                                        doc.RefreshToken = refreshToken;
                                        doc.Action = Enum.GetName(typeof(AuthenticationActions), action);
                                        doc.RedirectUrl = redirectUrl.IsDefaultOrNull() ? string.Empty : redirectUrl.ToString();
                                        await saveAsync(doc);
                                        return await onCreated(sessionId, token, refreshToken, action, redirectUrl);
                                    };
                                Func<string, Task<TResult>> onFailureWrapped =
                                    async (why) =>
                                    {
                                        doc.Message = $"LOOKUP CREATION FAILURE:{why}";
                                        await saveAsync(doc);
                                        return await onFailure(why);
                                    };
                                return await createMappingAsync(authorizationId, onCreateWrapped, onFailureWrapped);
                            };
                    var resp = await onContinue(createMappingWrappedAsync);
                    return resp;
                },
                application.AzureContext.DataContext.AzureStorageRepository);
            
        }

    }
}
