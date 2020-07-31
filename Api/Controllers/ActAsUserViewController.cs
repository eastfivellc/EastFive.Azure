//using BlackBarLabs.Extensions;
//using EastFive.Api;
//using EastFive.Api.Azure.Credentials;
//using EastFive.Api.Azure.Credentials.Controllers;
//using EastFive.Api.Controllers;
//using EastFive.Extensions;
//using EastFive.Linq.Async;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net.Http;
//using System.Threading.Tasks;
//using System.Web.Mvc;

//namespace EastFive.Security.SessionServer.Api.Controllers
//{
//    [FunctionViewController(Resource = typeof(CredentialProcessDocument), Route = "SessionManagement")]
//    public class ActAsUserViewController : Controller
//    {
//        public async Task<ActionResult> Index(string redirectUri, string token)
//        {
//            return View("~/Views/ActAsUser/");
//        }

//        public class SessionManagementDetails
//        {
//            public CredentialProcessDocument[] CredentialDocuments;
//            public IDictionary<Guid, string> AccountIdToNameLookup;
//        }

//        [EastFive.Api.HttpGet]
//        public static async Task<HttpResponseMessage> SessionManagement(
//                [OptionalQueryParameter(Name = "ApiKeySecurity")]string apiSecurityKey,
//                ApiSecurity apiSecurity,
//            EastFive.Api.Azure.AzureApplication application,
//            UnauthorizedResponse onUnauthorized,
//            ViewFileResponse viewResponse)
//        {
//            return await await CredentialProcessDocument.FindAllAsync(
//                async documents =>
//                {
//                    var orderedDocs = documents.OrderByDescending(doc => doc.Time).Take(1000).ToArray();

//                    var details = new SessionManagementDetails() { };
//                    details.CredentialDocuments = orderedDocs;
//                    details.AccountIdToNameLookup = await orderedDocs
//                        .Select(doc => doc.AuthorizationId)
//                        .Distinct()
//                        .Select(
//                            async authId =>
//                            {
//                                var fullName = await application.GetActorNameDetailsAsync(authId,
//                                    (username, firstName, lastName) =>
//                                    {
//                                        return $"{firstName} {lastName}";
//                                    },
//                                    () => string.Empty);
//                                return fullName.PairWithKey(authId);
//                            })
//                            .AsyncEnumerable()
//                            .ToDictionaryAsync();

//                    return viewResponse("/SessionManagement/Index.cshtml", details);
//                },
//                BlackBarLabs.Persistence.Azure.StorageTables.AzureStorageRepository.CreateRepository(
//                    EastFive.Azure.AppSettings.ASTConnectionStringKey));
//        }
        
//        [EastFive.Api.HttpGet]
//        public static async Task<HttpResponseMessage> ReplicateLogin(
//            [QueryParameter(Name = "credential_process_id")]Guid credentialProcessId,
//                [OptionalQueryParameter(Name = "ApiKeySecurity")]string apiSecurityKey,
//                ApiSecurity apiSecurity,
//            EastFive.Api.Azure.AzureApplication application, HttpRequestMessage request,
//            RedirectResponse redirectResponse,
//            ViewStringResponse viewResponse)
//        {
//            return await await CredentialProcessDocument.FindByIdAsync(credentialProcessId,
//                (document) =>
//                {
//                    return ResponseController.ProcessRequestAsync(application, document.Method, request.RequestUri, document.GetValuesCredential(),
//                        (redirectUri, message) => redirectResponse(redirectUri),
//                        (code, message, reason) => viewResponse($"<html><head><title>{reason}</title></head><body>{message}</body></html>", null));
//                },
//                () => viewResponse("", null).ToTask(),
//                BlackBarLabs.Persistence.Azure.StorageTables.AzureStorageRepository.CreateRepository(
//                    EastFive.Azure.AppSettings.ASTConnectionStringKey));
//        }

//        [EastFive.Api.HttpGet]
//        public static async Task<HttpResponseMessage> AuthenticationAsync(
//            [QueryParameter(Name = "authentication_process_id")]Guid credentialProcessId,
//                [OptionalQueryParameter(Name = "ApiKeySecurity")]string apiSecurityKey,
//                ApiSecurity apiSecurity,
//            EastFive.Api.Azure.AzureApplication application, HttpRequestMessage request,
//            RedirectResponse redirectResponse,
//            ViewStringResponse viewResponse)
//        {
//            return await await CredentialProcessDocument.FindByIdAsync(credentialProcessId,
//                (document) =>
//                {
//                    return ResponseController.AuthenticationAsync(credentialProcessId,
//                            document.Method, document.GetValuesCredential(), request.RequestUri,
//                            application,
//                        (redirectUri, message) => redirectResponse(redirectUri),
//                        (code, message, reason) => viewResponse($"<html><head><title>{reason}</title></head><body>{message}</body></html>", null));
//                },
//                () => viewResponse("", null).ToTask(),
//                BlackBarLabs.Persistence.Azure.StorageTables.AzureStorageRepository.CreateRepository(
//                    EastFive.Azure.AppSettings.ASTConnectionStringKey));
//        }

//        [EastFive.Api.HttpGet]
//        public static async Task<HttpResponseMessage> RedeemAsync(
//                [QueryParameter(Name = "redemption_process_id")]Guid credentialProcessId,
//                [OptionalQueryParameter(Name = "ApiKeySecurity")]string apiSecurityKey,
//                ApiSecurity apiSecurity,
//            EastFive.Api.Azure.AzureApplication application, HttpRequestMessage request,
//            RedirectResponse redirectResponse,
//            ViewStringResponse viewResponse)
//        {
//            return await await CredentialProcessDocument.FindByIdAsync(credentialProcessId,
//                async (document) =>
//                {
//                    var context = application.AzureContext;
//                    var responseParameters = document.GetValuesCredential();
//                    var providerKvp = await application.AuthorizationProviders
//                        .Where(prov => prov.Value.GetType().FullName == document.Provider)
//                        .FirstAsync(
//                            value => value,
//                            () => default(KeyValuePair<string, IProvideAuthorization>));
//                    var provider = providerKvp.Value;
//                    return await provider.ParseCredentailParameters(responseParameters,
//                        async (subject, stateId, loginId) => await await context.Sessions.TokenRedeemedAsync<Task<HttpResponseMessage>>(
//                            document.Method, provider, subject, stateId, loginId, responseParameters,
//                            (sessionId, authorizationId, token, refreshToken, actionReturned, providerReturned, extraParams, redirectUrl) =>
//                            {
//                                return ResponseController.CreateResponse(application, providerReturned, document.Method, actionReturned, sessionId, authorizationId,
//                                        token, refreshToken, extraParams, request.RequestUri, redirectUrl,
//                                    (redirectUri, message) => redirectResponse(redirectUri),
//                                    (code, message, reason) => viewResponse($"<html><head><title>{reason}</title></head><body>{message}</body></html>", null),
//                                    application.Telemetry);
//                            },
//                            async (redirectUrl, reason, providerReturned, extraParams) =>
//                            {
//                                if (redirectUrl.IsDefaultOrNull())
//                                    return Web.Configuration.Settings.GetUri(Security.SessionServer.Configuration.AppSettings.LandingPage,
//                                            (redirect) => redirectResponse(redirectUrl),
//                                            (why) => viewResponse($"<html><head><title>{reason}</title></head><body>{why}</body></html>", null));
//                                if (redirectUrl.Query.IsNullOrWhiteSpace())
//                                    redirectUrl = redirectUrl.SetQueryParam("cache", Guid.NewGuid().ToString("N"));
//                                return await redirectResponse(redirectUrl).AsTask();
//                            },
//                            (subjectReturned, credentialProvider, extraParams, createMappingAsync) =>
//                            {
//                                return ResponseController.UnmappedCredentailAsync(application,
//                                        credentialProvider, document.Method, subjectReturned, extraParams, request.RequestUri,
//                                        createMappingAsync,
//                                    (redirectUri, message) => redirectResponse(redirectUri),
//                                    (code, message, reason) => viewResponse($"<html><head><title>{reason}</title></head><body>{message}</body></html>", null),
//                                    application.Telemetry).ToTask();
//                            },
//                            (why) => viewResponse($"<html><head><title>{why}</title></head><body>{why}</body></html>", null).AsTask(),
//                            (why) => viewResponse($"<html><head><title>{why}</title></head><body>{why}</body></html>", null).AsTask(),
//                            (why) => viewResponse($"<html><head><title>{why}</title></head><body>{why}</body></html>", null).AsTask(),
//                            application.Telemetry),
//                        (why) => viewResponse($"<html><head><title>{why}</title></head><body>{why}</body></html>", null).AsTask());
//                },
//                () => viewResponse("", null).ToTask(),
//                BlackBarLabs.Persistence.Azure.StorageTables.AzureStorageRepository.CreateRepository(
//                    EastFive.Azure.AppSettings.ASTConnectionStringKey));
//        }

//        [EastFive.Api.HttpGet]
//        public static async Task<HttpResponseMessage> CreateResponseAsync(
//                [QueryParameter(Name = "login_process_id")]Guid credentialProcessId,
//                [OptionalQueryParameter(Name = "ApiKeySecurity")]string apiSecurityKey,
//                ApiSecurity apiSecurity,
//            EastFive.Api.Azure.AzureApplication application, HttpRequestMessage request,
//            RedirectResponse redirectResponse,
//            ViewStringResponse viewResponse)
//        {
//            return await await CredentialProcessDocument.FindByIdAsync(credentialProcessId,
//                async (document) =>
//                {
//                    var providerKvp = await application.AuthorizationProviders
//                        .Where(prov => prov.Value.GetType().FullName == document.Provider)
//                        .FirstAsync(
//                            value => value,
//                            () => default(KeyValuePair<string, IProvideAuthorization>));
//                    var provider = providerKvp.Value;
//                    Enum.TryParse(document.Action, out AuthenticationActions action);
//                    return await ResponseController.CreateResponse(application, provider, document.Method, action,
//                            document.SessionId, document.AuthorizationId, document.Token, document.RefreshToken,
//                            document.GetValuesCredential(), request.RequestUri, 
//                            document.RedirectUrl.IsNullOrWhiteSpace(
//                                () => null,
//                                redirUrlString => new Uri(redirUrlString)),
//                        (redirectUri, message) => redirectResponse(redirectUri),
//                        (code, message, reason) => viewResponse($"<html><head><title>{reason}</title></head><body>{message}</body></html>", null),
//                        application.Telemetry);
//                },
//                () => viewResponse("", null).AsTask(),
//                BlackBarLabs.Persistence.Azure.StorageTables.AzureStorageRepository.CreateRepository(
//                    EastFive.Azure.AppSettings.ASTConnectionStringKey));
//        }

//    }
    
//}
