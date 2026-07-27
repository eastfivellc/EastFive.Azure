using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using EastFive;
using EastFive.Api;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;

namespace EastFive.Azure.OAuth.Server
{
    /// <summary>
    /// GET /oauth/authorize — OAuth 2.1 authorization endpoint (RFC 6749 §4.1.1 + PKCE).
    /// The user is authenticated by federating into the existing EastFive login flow
    /// (Authorization/Method/Redirection — i.e. Google or any registered IProvideLogin);
    /// no local passwords are involved. Sub-actions:
    ///   GET  /oauth/authorize/login   — begin login with a chosen method
    ///   GET  /oauth/authorize/resume  — return leg after the login provider completes
    ///   POST /oauth/authorize/approve — consent form submission → authorization code
    /// </summary>
    [FunctionViewController(
        Namespace = "oauth",
        Route = "authorize",
        ContentType = "text/html")]
    public class OAuthAuthorize
    {
        public const string OAuthRequestParameterName = "oauth_request";
        private static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(90);

        #region GET /oauth/authorize (entry)

        [Unsecured("OAuth 2.1 authorization endpoint - authenticates the resource owner via the existing federated login flow before issuing an authorization code.")]
        [HttpGet(MatchAllParameters = false)]
        public static async Task<IHttpResponse> AuthorizeAsync(
                IHttpRequest request,
                IApplication application,
            HtmlResponse onPage,
            RedirectResponse onRedirect)
        {
            var query = ParseQuery(request.RequestUri);
            var clientId = Param(query, "client_id");
            var redirectUriString = Param(query, "redirect_uri");
            var responseType = Param(query, "response_type");
            var state = Param(query, "state");
            var scope = Param(query, "scope");
            var codeChallenge = Param(query, "code_challenge");
            var codeChallengeMethod = Param(query, "code_challenge_method");
            var resource = Param(query, "resource");

            if (clientId.IsNullOrWhiteSpace())
                return ErrorPage(onPage, "invalid_request", "client_id is required.");

            return await await LoadClientAsync(clientId,
                async client =>
                {
                    if (!client.isActive)
                        return ErrorPage(onPage, "unauthorized_client", "This client has been deactivated.");

                    // redirect_uri must be pre-registered (exact string match) before it is trusted
                    if (redirectUriString.IsNullOrWhiteSpace())
                        return ErrorPage(onPage, "invalid_request", "redirect_uri is required.");
                    if (!Uri.TryCreate(redirectUriString, UriKind.Absolute, out var redirectUri)
                            || !OAuthServer.IsAllowableRedirectUri(redirectUri)
                            || !OAuthServer.RedirectUriIsRegistered(redirectUriString, client.redirectUris))
                        return ErrorPage(onPage, "invalid_request",
                            "redirect_uri does not match a registered redirect uri for this client.");

                    // From here errors are safe to deliver to the (validated) redirect_uri per RFC 6749 §4.1.2.1
                    if (responseType != "code")
                        return RedirectError(onRedirect, redirectUri, state,
                            "unsupported_response_type", "Only the `code` response type is supported.");

                    // OAuth 2.1: PKCE S256 required (all dynamically registered MCP clients are public)
                    if (codeChallenge.IsNullOrWhiteSpace())
                        return RedirectError(onRedirect, redirectUri, state,
                            "invalid_request", "code_challenge is required (PKCE, RFC 7636).");
                    var challengeMethod = codeChallengeMethod.HasBlackSpace()
                        ? codeChallengeMethod
                        : OAuthServer.CodeChallengeMethodS256;
                    if (challengeMethod != OAuthServer.CodeChallengeMethodS256)
                        return RedirectError(onRedirect, redirectUri, state,
                            "invalid_request", "Only the S256 code_challenge_method is supported.");

                    var authorizeRequest = new OAuthAuthorizeRequest
                    {
                        @ref = Guid.NewGuid().AsRef<OAuthAuthorizeRequest>(),
                        clientId = clientId,
                        redirectUri = redirectUriString,
                        state = state,
                        scope = scope,
                        codeChallenge = codeChallenge,
                        codeChallengeMethod = challengeMethod,
                        resource = resource,
                        status = OAuthAuthorizeRequest.StatusPending,
                        createdOn = DateTime.UtcNow,
                    };

                    return await authorizeRequest.StorageCreateAsync(
                        created => MethodPickerPage(onPage, request.RequestUri,
                            application, authorizeRequest, client),
                        onAlreadyExists: () => ErrorPage(onPage, "server_error", "Please retry the request."));
                },
                onNotFound: () => ErrorPage(onPage, "unauthorized_client",
                    "Unknown client_id. Register the client first (POST /oauth/register).").AsTask());
        }

        #endregion

        #region GET /oauth/authorize/login (begin login with a chosen method)

        [Unsecured("Begins the federated login hop for a pending OAuth authorization request; redirects to the chosen identity provider.")]
        [HttpAction("login", MatchAllParameters = false)]
        public static async Task<IHttpResponse> LoginAsync(
                [QueryParameter(Name = OAuthRequestParameterName)] IRef<OAuthAuthorizeRequest> requestRef,
                [QueryParameter(Name = "method")] IRef<Method> methodRef,
                IHttpRequest request,
                IApplication application,
                IProvideUrl urlHelper,
            RedirectResponse onRedirect,
            HtmlResponse onPage)
        {
            return await await requestRef.StorageGetAsync(
                async authorizeRequest =>
                {
                    if (!IsUsable(authorizeRequest, OAuthAuthorizeRequest.StatusPending))
                        return ErrorPage(onPage, "invalid_request",
                            "This authorization request has expired. Return to the application and retry.");

                    return await await Method.ById(methodRef, application,
                        async method =>
                        {
                            var resumeUri = new Uri(request.RequestUri,
                                $"/oauth/authorize/resume?{OAuthRequestParameterName}={requestRef.id}");
                            var loginAuthorizationRef = Guid.NewGuid().AsRef<EastFive.Azure.Auth.Authorization>();
                            var loginAuthorization = new EastFive.Azure.Auth.Authorization
                            {
                                authorizationRef = loginAuthorizationRef,
                                Method = methodRef,
                                LocationAuthenticationReturn = resumeUri,
                                authorized = false,
                            };
                            loginAuthorization.LocationAuthentication = await method.GetLoginUrlAsync(
                                application, urlHelper, loginAuthorizationRef.id);

                            return await loginAuthorization.StorageCreateAsync(
                                created => onRedirect(loginAuthorization.LocationAuthentication),
                                onAlreadyExists: () => ErrorPage(onPage, "server_error", "Please retry the request."));
                        },
                        () => ErrorPage(onPage, "server_error",
                            "That login method is not available.").AsTask());
                },
                () => ErrorPage(onPage, "invalid_request",
                    "Unknown authorization request.").AsTask());
        }

        #endregion

        #region GET /oauth/authorize/resume (return from the login provider → consent)

        [Unsecured("Return leg from the federated login provider; resolves the authenticated account and renders the consent page.")]
        [HttpAction("resume", MatchAllParameters = false)]
        public static async Task<IHttpResponse> ResumeAsync(
                [QueryParameter(Name = OAuthRequestParameterName)] IRef<OAuthAuthorizeRequest> requestRef,
                [QueryParameter(Name = EastFive.Api.Azure.AzureApplication.QueryRequestIdentfier)]
                    IRef<EastFive.Azure.Auth.Authorization> loginAuthorizationRef,
                IApplication application,
            HtmlResponse onPage)
        {
            return await await requestRef.StorageGetAsync(
                async authorizeRequest =>
                {
                    if (!IsUsable(authorizeRequest, OAuthAuthorizeRequest.StatusPending))
                        return ErrorPage(onPage, "invalid_request",
                            "This authorization request has expired. Return to the application and retry.");

                    return await await Session.GetClaimsAsync(application, loginAuthorizationRef.Optional(),
                        async (claims, accountIdMaybe, authorized, sessionExpiresOnMaybe) =>
                        {
                            if (!accountIdMaybe.HasValue || !authorized)
                                return ErrorPage(onPage, "access_denied",
                                    "Sign-in did not complete. Return to the application and retry.");

                            var approvalKey = OAuthServer.GenerateSecret();
                            return await requestRef.StorageUpdateAsync2(
                                toUpdate =>
                                {
                                    toUpdate.authorization = loginAuthorizationRef.Optional();
                                    toUpdate.accountId = accountIdMaybe.Value;
                                    toUpdate.approvalKey = OAuthServer.ComputeSecretHash(approvalKey);
                                    toUpdate.status = OAuthAuthorizeRequest.StatusConsentPending;
                                    return toUpdate;
                                },
                                updated => ConsentPage(onPage, updated, approvalKey),
                                () => ErrorPage(onPage, "invalid_request", "Unknown authorization request."));
                        },
                        why => ErrorPage(onPage, "access_denied",
                            $"Sign-in could not be verified: {why}").AsTask());
                },
                () => ErrorPage(onPage, "invalid_request",
                    "Unknown authorization request.").AsTask());
        }

        #endregion

        #region POST /oauth/authorize/approve (consent decision → code)

        [Unsecured("Consent form submission for a pending OAuth authorization request; the one-time approval key issued after login binds the decision to the authenticated browser.")]
        [HttpAction("POST", "approve")]
        public static async Task<IHttpResponse> ApproveAsync(
                IHttpRequest request,
            RedirectResponse onRedirect,
            HtmlResponse onPage)
        {
            var form = request.Form;
            if (form.IsDefaultOrNull())
                return ErrorPage(onPage, "invalid_request", "Form submission expected.");
            var requestIdString = (string)form[OAuthRequestParameterName];
            var approvalKey = (string)form["approval_key"];
            var decision = (string)form["decision"];

            if (!Guid.TryParse(requestIdString, out var requestId))
                return ErrorPage(onPage, "invalid_request", "Unknown authorization request.");
            var requestRef = requestId.AsRef<OAuthAuthorizeRequest>();

            return await await requestRef.StorageGetAsync(
                async authorizeRequest =>
                {
                    if (!IsUsable(authorizeRequest, OAuthAuthorizeRequest.StatusConsentPending))
                        return ErrorPage(onPage, "invalid_request",
                            "This authorization request has expired. Return to the application and retry.");
                    if (approvalKey.IsNullOrWhiteSpace()
                            || !OAuthServer.SecretMatchesHash(approvalKey, authorizeRequest.approvalKey))
                        return ErrorPage(onPage, "access_denied", "Consent could not be verified.");

                    var redirectUri = new Uri(authorizeRequest.redirectUri, UriKind.Absolute);

                    // single-use: consume the request regardless of the decision
                    var consumed = await requestRef.StorageUpdateAsync2(
                        toUpdate =>
                        {
                            if (toUpdate.status != OAuthAuthorizeRequest.StatusConsentPending)
                                return toUpdate;
                            toUpdate.status = OAuthAuthorizeRequest.StatusConsumed;
                            return toUpdate;
                        },
                        updated => true,
                        () => false);
                    if (!consumed)
                        return ErrorPage(onPage, "invalid_request", "Unknown authorization request.");

                    if (decision != "approve")
                        return RedirectError(onRedirect, redirectUri, authorizeRequest.state,
                            "access_denied", "The user denied the request.");

                    var code = OAuthServer.GenerateSecret();
                    var codeRecord = new OAuthAuthorizationCode
                    {
                        @ref = OAuthServer.ComputeLookupGuid(code).AsRef<OAuthAuthorizationCode>(),
                        codeHash = OAuthServer.ComputeSecretHash(code),
                        clientId = authorizeRequest.clientId,
                        redirectUri = authorizeRequest.redirectUri,
                        scope = authorizeRequest.scope,
                        codeChallenge = authorizeRequest.codeChallenge,
                        resource = authorizeRequest.resource,
                        authorization = authorizeRequest.authorization,
                        accountId = authorizeRequest.accountId.Value,
                        expiresOn = DateTime.UtcNow + CodeLifetime,
                        consumed = false,
                    };
                    return await codeRecord.StorageCreateAsync(
                        created =>
                        {
                            var location = redirectUri.AddQueryParameter("code", code);
                            if (authorizeRequest.state.HasBlackSpace())
                                location = location.AddQueryParameter("state", authorizeRequest.state);
                            return onRedirect(location);
                        },
                        onAlreadyExists: () => ErrorPage(onPage, "server_error", "Please retry the request."));
                },
                () => ErrorPage(onPage, "invalid_request",
                    "Unknown authorization request.").AsTask());
        }

        #endregion

        #region Helpers

        internal static Task<TResult> LoadClientAsync<TResult>(string clientId,
            Func<ClientCredential, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return clientId
                .StorageGetBy((ClientCredential client) => client.clientId)
                .FirstAsync(
                    client => onFound(client),
                    () => onNotFound());
        }

        private static bool IsUsable(OAuthAuthorizeRequest authorizeRequest, string expectedStatus)
        {
            if (authorizeRequest.status != expectedStatus)
                return false;
            return authorizeRequest.createdOn + RequestLifetime > DateTime.UtcNow;
        }

        private static string Param(IDictionary<string, string> query, string key) =>
            query.TryGetValue(key, out var value) ? value : default;

        private static IDictionary<string, string> ParseQuery(Uri uri)
        {
            return uri.ParseQuery()
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        private static IHttpResponse RedirectError(RedirectResponse onRedirect,
            Uri redirectUri, string state, string error, string description)
        {
            var location = redirectUri
                .AddQueryParameter("error", error)
                .AddQueryParameter("error_description", description);
            if (state.HasBlackSpace())
                location = location.AddQueryParameter("state", state);
            return onRedirect(location);
        }

        #endregion

        #region HTML pages

        private static IHttpResponse ErrorPage(HtmlResponse onPage, string error, string description)
        {
            var html = Page($@"
    <h1>Authorization error</h1>
    <p class=""error"">{HtmlEncode(error)}</p>
    <p>{HtmlEncode(description)}</p>");
            var response = onPage(html);
            response.StatusCode = HttpStatusCode.BadRequest;
            return response;
        }

        private static IHttpResponse MethodPickerPage(HtmlResponse onPage, Uri requestUri,
            IApplication application, OAuthAuthorizeRequest authorizeRequest, ClientCredential client)
        {
            var methodLinks = application.GetLoginProviders()
                .Select(
                    loginProvider =>
                    {
                        var methodId = loginProvider.Value.Id;
                        var methodName = loginProvider.Value.Method;
                        var href = $"/oauth/authorize/login?{OAuthRequestParameterName}={authorizeRequest.id}&method={methodId}";
                        return $@"<a class=""method"" href=""{HtmlEncode(href)}"">Continue with {HtmlEncode(methodName)}</a>";
                    })
                .Join("\n    ");

            var html = Page($@"
    <h1>Sign in to continue</h1>
    <p><strong>{HtmlEncode(client.name)}</strong> is requesting access{ScopeSummary(authorizeRequest.scope)}.</p>
    <p>Choose how to sign in:</p>
    {methodLinks}");
            return onPage(html);
        }

        private static IHttpResponse ConsentPage(HtmlResponse onPage,
            OAuthAuthorizeRequest authorizeRequest, string approvalKey)
        {
            var scopeList = authorizeRequest.scope.HasBlackSpace()
                ? authorizeRequest.scope
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => $"<li>{HtmlEncode(s)}</li>")
                    .Join("")
                : "<li>Access the API on your behalf</li>";

            var html = Page($@"
    <h1>Authorize access</h1>
    <p>Allow this application to:</p>
    <ul>{scopeList}</ul>
    <form method=""post"" action=""/oauth/authorize/approve"">
        <input type=""hidden"" name=""{OAuthRequestParameterName}"" value=""{authorizeRequest.id}"" />
        <input type=""hidden"" name=""approval_key"" value=""{HtmlEncode(approvalKey)}"" />
        <button type=""submit"" name=""decision"" value=""approve"" class=""approve"">Allow</button>
        <button type=""submit"" name=""decision"" value=""deny"" class=""deny"">Deny</button>
    </form>");
            return onPage(html);
        }

        private static string ScopeSummary(string scope) =>
            scope.HasBlackSpace() ? $" to: <code>{HtmlEncode(scope)}</code>" : "";

        private static string HtmlEncode(string text) =>
            WebUtility.HtmlEncode(text ?? string.Empty);

        private static string Page(string body) => $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
    <title>Authorize</title>
    <style>
        body {{ font-family: -apple-system, 'Segoe UI', Roboto, sans-serif; max-width: 26rem;
               margin: 4rem auto; padding: 0 1rem; color: #1a202c; }}
        h1 {{ font-size: 1.3rem; }}
        .error {{ color: #c53030; font-weight: 600; }}
        a.method {{ display: block; margin: .5rem 0; padding: .6rem 1rem; border: 1px solid #cbd5e0;
                    border-radius: .4rem; text-decoration: none; color: #2b6cb0; }}
        a.method:hover {{ background: #ebf8ff; }}
        button {{ padding: .55rem 1.4rem; border-radius: .4rem; border: 1px solid #cbd5e0;
                  font-size: 1rem; cursor: pointer; margin-right: .6rem; }}
        button.approve {{ background: #2b6cb0; border-color: #2b6cb0; color: #fff; }}
        button.deny {{ background: #fff; }}
        ul {{ padding-left: 1.2rem; }}
    </style>
</head>
<body>{body}
</body>
</html>";

        #endregion
    }
}
