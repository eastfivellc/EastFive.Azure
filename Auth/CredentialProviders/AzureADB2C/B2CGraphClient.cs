using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Web.Configuration;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EastFive.AzureADB2C
{
    public class B2CGraphClient : IDisposable
    {
        private readonly string tenant;
        private HttpClient http;
        
        private B2CGraphClient(string clientId, string clientSecret, string tenant)
        {
            this.tenant = tenant;
            //var wrh = new WebRequestHandler()
            //{
            //    ReadWriteTimeout = (int)TimeSpan.FromMinutes(5).TotalMilliseconds
            //};
            this.http = new HttpClient(
                new HttpRetryHandler(5, 2, 
                    new RefreshTokenMessageHandler(
                    // The AuthenticationContext is ADAL's primary class, in which you indicate the directory to use.
                    Globals.aadInstance + tenant,
                    Globals.aadGraphResourceId,
                    // The ClientCredential is where you pass in your client_id and client_secret, which are 
                    // provided to Azure AD in order to receive an access_token using the app's identity.
                    clientId,
                    clientSecret //,
                    //wrh
                    )), true)
            {
                Timeout = new TimeSpan(0, 5, 0)
            };
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (http != null)
                {
                    http.Dispose();
                    http = null;
                }
            }
        }

        ~B2CGraphClient()
        {
            Dispose(false);
        }

        private class HttpRetryHandler : DelegatingHandler
        {
            private readonly int attempts;
            private readonly int delaySeconds;

            public HttpRetryHandler(int attempts, int delaySeconds, HttpMessageHandler innerHandler = default(HttpMessageHandler))
                : base()
            {
                this.InnerHandler = innerHandler.IsDefaultOrNull() ? new HttpClientHandler() : innerHandler;
                this.attempts = attempts;
                this.delaySeconds = delaySeconds;
            }

            private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken, int attemptsRemaining, int delaySeconds)
            {
                try
                {
                    --attemptsRemaining;
                    return await base.SendAsync(request, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    if (attemptsRemaining > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                        return await SendAsync(await CloneAsync(request), cancellationToken, attemptsRemaining, delaySeconds * 2);
                    }
                    throw;
                }
                catch (HttpRequestException e)
                {
                    Exception inner = e;
                    while (inner.InnerException != null) inner = inner.InnerException;
                    if (inner.Message.Contains("remote name could not be resolved") && attemptsRemaining > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                        return await SendAsync(await CloneAsync(request), cancellationToken, attemptsRemaining, delaySeconds * 2);
                    }
                    throw;
                }
            }

            private async Task<HttpContent> CloneAsync(HttpContent content)
            {
                if (content == null)
                    return null;

                var ms = new MemoryStream();
                await content.CopyToAsync(ms);
                ms.Position = 0;
                var clone = new StreamContent(ms);
                content.Headers.ToList().ForEach(x => clone.Headers.Add(x.Key, x.Value));
                return clone;
            }

            private async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
            {
                var clone = new HttpRequestMessage(request.Method, request.RequestUri)
                {
                    Content = await CloneAsync(request.Content),
                    Version = request.Version
                };
                request.Properties.ToList().ForEach(x => clone.Properties.Add(x));
                request.Headers.ToList().ForEach(x => clone.Headers.TryAddWithoutValidation(x.Key, x.Value));
                return request;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return SendAsync(request, cancellationToken, attempts, delaySeconds);
            }
        }

        private class RefreshTokenMessageHandler : EastFive.Net.Http.RefreshTokenMessageHandler
        {
            private readonly string authority;
            private readonly string tokenResource;
            private readonly string clientId;
            private readonly string clientSecret;
            private readonly TimeSpan expireBuffer = TimeSpan.FromMinutes(5);
            private SemaphoreSlim cache = new SemaphoreSlim(1, 1);

            private Int64 expiresUtc;
            private string accessToken;

            public RefreshTokenMessageHandler(string authority, string tokenResource, string clientId, string clientSecret, HttpMessageHandler innerHandler = default(HttpMessageHandler))
                : base(innerHandler)
            {
                this.authority = authority;
                this.tokenResource = tokenResource;
                this.clientId = clientId;
                this.clientSecret = clientSecret;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (cache != null)
                    {
                        cache.Dispose();
                        cache = null;
                    }
                }

                base.Dispose(disposing);
            }

            ~RefreshTokenMessageHandler()
            {
                Dispose(false);
            }

            protected override string AccessToken => accessToken;

            protected override async Task<bool> NeedsRefreshAsync(HttpResponseMessage response)
            {
                return await base.NeedsRefreshAsync(response) || !IsTokenValid();
            }

            private bool IsTokenValid()
            {
                return DateTime.UtcNow.Ticks < Interlocked.Read(ref expiresUtc);
            }

            protected override async Task<TResult> RefreshTokenAsync<TResult>(Func<string, TResult> onSuccess, Func<string, TResult> onFailure)
            {
                if (IsTokenValid())
                    return onSuccess(this.accessToken);

                await cache.WaitAsync();
                if (IsTokenValid())
                {
                    cache.Release();
                    return onSuccess(this.accessToken);
                }

                AuthenticationResult result = null;
                try
                {
                    var authContext = new AuthenticationContext(authority, true);
                    var credential = new ClientCredential(clientId, clientSecret);
                    result = await authContext.AcquireTokenAsync(tokenResource, credential);
                    string accessToken = result.AccessToken;
                    long expiresUtc = result.ExpiresOn.Ticks - expireBuffer.Ticks;
                    Interlocked.Exchange(ref this.accessToken, accessToken);
                    Interlocked.Exchange(ref this.expiresUtc, expiresUtc);
                    cache.Release();
                    return onSuccess(accessToken);
                }
                catch (Exception e)
                {
                    cache.Release();
                    return onFailure(e.Message);
                }
            }
        }

        internal static TResult LoadFromConfig<TResult>(
            Func<B2CGraphClient, TResult> onSuccess,
            Func<string, TResult> onConfigurationNotAvailable)
        {
            // The client_id, client_secret, and tenant are pulled in from the App.config file
            return Azure.AppSettings.AzureADB2C.ClientId.ConfigurationString(
                   (clientId) =>
                   {
                       return Azure.AppSettings.AzureADB2C.ClientSecret.ConfigurationString(
                           (clientSecret) =>
                           {
                               return Azure.AppSettings.AzureADB2C.Tenant.ConfigurationString(
                                   (tenant) =>
                                   {
                                       try
                                       {
                                           var client = new B2CGraphClient(clientId, clientSecret, tenant);
                                           return onSuccess(client);
                                       } catch(Exception ex)
                                       {
                                           return onConfigurationNotAvailable(ex.Message);
                                       }
                                   },
                                   onConfigurationNotAvailable);
                           },
                           onConfigurationNotAvailable);
                   },
                   onConfigurationNotAvailable);
        }

        public Task<TResult> GetUserByObjectId<TResult>(string objectId,
            Func<string, string, bool, string, bool, bool, TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<string, TResult> onFailure)
        {
            return SendGraphGetRequest("/users/" + objectId, null, 
                (userResult) =>
                {
                    try
                    {
                        var user = JsonConvert.DeserializeObject<Resources.User>(userResult);
                        var signinName = default(string);
                        var displayName = default(string);
                        var isEmail = default(bool);
                        var otherMail = default(string);
                        if (default(Resources.User.SignInName[]) != user.SignInNames &&
                            user.SignInNames.Length > 0)
                        {
                            displayName = user.DisplayName;
                            signinName = user.SignInNames[0].Value;
                            isEmail = String.Compare(user.SignInNames[0].Type, "emailAddress") == 0;
                            otherMail = user.OtherMails?.FirstOrDefault();
                        }
                        var forceChange = default(Resources.User.PasswordProfileResource) != user.PasswordProfile
                            ? user.PasswordProfile.ForceChangePasswordNextLogin
                            : default(bool);

                        return onSuccess(displayName, signinName, isEmail, otherMail, forceChange, user.AccountEnabled);
                    }
                    catch(Exception e)
                    {
                        return onFailure(e.Message);
                    }
                },
                (statusCode, reasonPhrase, error) =>
                {
                    if (error?.OdataError?.Code == "Request_ResourceNotFound")
                        return onNotFound();
                    return onFailure(error?.OdataError?.Message?.Value ?? reasonPhrase);
                });
        }

        public async Task<TResult> GetUserByUserIdAsync<TResult>(string userId,
            Func<Guid, bool, bool, bool, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            return await SendGraphGetRequest("/users/", 
                $"$filter=signInNames/any(x:x/value eq '{userId}')",
                (userResult) =>
                {
                    var userData = JsonConvert.DeserializeObject<Resources.ODataMetadata<Resources.User[]>>(userResult);
                    if (default(Resources.ODataMetadata<Resources.User[]>) == userData)
                        return onFailure("Could not parse result data");
                    var users = userData.Value.NullToEmpty().ToArray();
                    if (users.Length < 1)
                        return onFailure("User not found");
                    var user = ParseUser(users[0]);
                    if (null == user)
                        return onFailure("Could not parse user");
                    return onSuccess(user.Item1, user.Item4, user.Item6, user.Item6);
                },
                (statusCode, reasonPhrase, error) =>
                {
                    return onFailure(error?.OdataError?.Message?.Value ?? reasonPhrase);
                });
        }

        private Tuple<Guid, string, string, bool, string, bool, bool> ParseUser(Resources.User user)
        {
            Guid loginId;
            if (!Guid.TryParse(user.ObjectId, out loginId))
                return default(Tuple<Guid, string, string, bool, string, bool, bool>);
            var displayName = string.Empty;
            var isEmail = false;
            var userName = string.Empty;
            var otherMail = default(string);
            if (default(Resources.User.SignInName[]) != user.SignInNames &&
                user.SignInNames.Length > 0)
            {
                displayName = user.DisplayName;
                isEmail = String.Compare(user.SignInNames[0].Type, "emailAddress") == 0;
                userName = user.SignInNames[0].Value;
                otherMail = user.OtherMails?.FirstOrDefault();
            }
            var forceChange = default(Resources.User.PasswordProfileResource) != user.PasswordProfile && user.PasswordProfile.ForceChangePasswordNextLogin;
            return new Tuple<Guid, string, string, bool, string, bool, bool>(loginId,displayName,userName,isEmail,otherMail,forceChange,user.AccountEnabled);
        }

        public async Task<TResult> GetAllUsersAsync<TResult>(
            Action<Tuple<Guid,string,string,bool,string,bool,bool>[]> onSegment,
            Func<TResult> onSuccess,
            Func<string,TResult> onFailure)
        {
            const string skipToken = "$skiptoken=X'";
            var query = string.Empty;
            do
            {
                var userResponse = await SendGraphGetRequest("/users", query,
                    (resp) => resp,
                    (statusCode, reasonPhrase, error) => string.Empty);
                var userPayload = JsonConvert.DeserializeObject<Resources.ODataMetadata<Resources.User[]>>(userResponse);
                if (null == userPayload)
                    return onFailure("Could not parse result data");

                var userValues = userPayload.Value.NullToEmpty().ToArray();
                if (userValues.Length == 0)
                    break;

                onSegment(userValues
                    .Select(ParseUser)
                    .SelectWhereNotNull()
                    .ToArray());

                var index = userResponse.LastIndexOf(skipToken);
                if (index == -1)
                    break;
                query = userResponse.Substring(index, userResponse.IndexOf("'", index + skipToken.Length) + 1 - index);
            } while (true);
            return onSuccess();
        }

        public async Task<string> GetAllUsers(string query)
        {
            return await SendGraphGetRequest("/users", query,
                (result) => result,
                (statusCode, reasonPhrase, error) => throw new WebException("Error Calling the Graph API: \n" + JsonConvert.SerializeObject(error, Formatting.Indented)));
        }

        public async Task<TResult> CreateUser<TResult>(string displayName,
            string userId, bool isEmail, string secret, bool forceChange,
            Func<Guid, TResult> onSuccess,
            Func<Guid, TResult> onUserIdAlreadyExists,
            Func<TResult> onPasswordInsufficent,
            Func<string, TResult> onFail)
        {
            var user = new EastFive.AzureADB2C.Resources.User()
            {
                DisplayName = displayName,
                AccountEnabled = true,
                SignInNames = new[] {
                    new EastFive.AzureADB2C.Resources.User.SignInName
                    {
                        Type = isEmail? "emailAddress" : "userName",
                        Value = userId,
                    }
                },
                PasswordProfile = new EastFive.AzureADB2C.Resources.User.PasswordProfileResource
                {
                    ForceChangePasswordNextLogin = forceChange,
                    Password = secret,
                },
                OtherMails = isEmail ? new[] { userId } : new string[] { }
            };
            var json = JsonConvert.SerializeObject(user);
            var result = await await SendGraphPostRequest("/users", json,
                (resultJson) =>
                {
                    var resultUser = JsonConvert.DeserializeObject<Resources.User>(resultJson);
                    Guid objectId;
                    if (Guid.TryParse(resultUser.ObjectId, out objectId))
                        return onSuccess(objectId).AsTask();
                    return onFail("Could not parse ID in response").AsTask();
                },
                async (code, reason, issue) =>
                {
                    if (
                        (String.Compare("Request_BadRequest", issue.OdataError.Code) == 0)
                        &&
                        issue.OdataError.Values.NullToEmpty().Any(errorValue =>
                            (String.Compare("PropertyName", errorValue.Item) == 0 &&
                             String.Compare("signInNames", errorValue.Value) == 0)))
                    {
                        return await GetUserByUserIdAsync(userId,
                            (loginId, isEmailCurrent, forceChangeCurrent, accountEnabled) => onUserIdAlreadyExists(loginId),
                            (why) => onUserIdAlreadyExists(default(Guid)));
                    }
                    if (
                        (String.Compare("Request_BadRequest", issue.OdataError.Code) == 0)
                        &&
                        default(Resources.ODataError.Error.MessageType) != issue.OdataError.Message
                        &&
                        !String.IsNullOrWhiteSpace(issue.OdataError.Message.Value)
                        &&
                        issue.OdataError.Message.Value.Contains("The specified password does not comply with password complexity requirements"))
                    {
                        return onPasswordInsufficent();
                    }
                    return onFail($"{reason} : {issue.OdataError.Message.Value}");
                });
            return result;
        }

        public async Task<TResult> UpdateUserPasswordAsync<TResult>(string objectId, string password, bool forceChange,
            Func<string,TResult> onSuccess,
            Func<string,TResult> onFailure)
        {
            var user = new EastFive.AzureADB2C.Resources.UserPasswordPatch()
            {
                ObjectId = objectId,
                PasswordProfile = new EastFive.AzureADB2C.Resources.User.PasswordProfileResource
                {
                    ForceChangePasswordNextLogin = forceChange,
                    Password = password,
                }
            };
            var json = JsonConvert.SerializeObject(user);
            return await SendGraphPatchRequest("/users/" + objectId, json,
                onSuccess, onFailure);
        }

        public async Task<TResult> UpdateUserEmailAsync<TResult>(string objectId, string email,
            Func<string, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            var user = new EastFive.AzureADB2C.Resources.UserEmailPatch()
            {
                ObjectId = objectId,
                OtherMails = string.IsNullOrWhiteSpace(email) ? new string[] { } : new [] { email }
            };
            var json = JsonConvert.SerializeObject(user);
            return await SendGraphPatchRequest("/users/" + objectId, json,
                onSuccess, onFailure);
        }

        public async Task<string> DeleteUser(string objectId)
        {
            return await SendGraphDeleteRequest("/users/" + objectId);
        }

        public async Task<TResult> RegisterExtension<TResult>(string objectId, string body,
            Func<TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            return await SendGraphPostRequest("/applications/" + objectId + "/extensionProperties", body,
                (responseBody) => onSuccess(),
                (code, why, error) => onFailure(why));
        }

        public async Task<string> UnregisterExtension(string appObjectId, string extensionObjectId)
        {
            return await SendGraphDeleteRequest("/applications/" + appObjectId + "/extensionProperties/" + extensionObjectId);
        }

        public async Task<string> GetExtensions(string appObjectId)
        {
            return await SendGraphGetRequest("/applications/" + appObjectId + "/extensionProperties", null,
                (result) => result,
                (statusCode, reasonPhrase, error) => throw new WebException("Error Calling the Graph API: \n" + JsonConvert.SerializeObject(error, Formatting.Indented)));
        }

        public async Task<string> GetApplications(string query)
        {
            return await SendGraphGetRequest("/applications", query,
                (result) => result,
                (statusCode, reasonPhrase, error) => throw new WebException("Error Calling the Graph API: \n" + JsonConvert.SerializeObject(error, Formatting.Indented)));
        }

        private async Task<string> SendGraphDeleteRequest(string api)
        {
            // NOTE: This client uses ADAL v2, not ADAL v4
            string url = Globals.aadGraphEndpoint + tenant + api + "?" + Globals.aadGraphVersion;
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, url))
            {
                using (HttpResponseMessage response = await http.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string error = await response.Content.ReadAsStringAsync();
                        object formatted = JsonConvert.DeserializeObject(error);
                        throw new WebException("Error Calling the Graph API: \n" + JsonConvert.SerializeObject(formatted, Formatting.Indented));
                    }
                    return await response.Content.ReadAsStringAsync();
                }
            }
        }

        private async Task<TResult> SendGraphPatchRequest<TResult>(string api, string json,
            Func<string,TResult> onSuccess,
            Func<string,TResult> onFailure)
        {
            // NOTE: This client uses ADAL v2, not ADAL v4
            string url = Globals.aadGraphEndpoint + tenant + api + "?" + Globals.aadGraphVersion;

            using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("PATCH"), url))
            {
                request.Content = new StringContent(json, new UTF8Encoding(false), "application/json");
                using (HttpResponseMessage response = await http.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string error = await response.Content.ReadAsStringAsync();
                        object formatted = JsonConvert.DeserializeObject(error);
                        return onFailure(error);
                    }
                    return onSuccess(await response.Content.ReadAsStringAsync());
                }
            }
        }

        private async Task<TResult> SendGraphPostRequest<TResult>(string api, string json,
            Func<string, TResult> onSuccess,
            Func<HttpStatusCode, string, Resources.ODataError, TResult> onFailed)
        {
            string url = Globals.aadGraphEndpoint + tenant + api + "?" + Globals.aadGraphVersion;

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = new StringContent(json, new UTF8Encoding(false), "application/json");
                using (HttpResponseMessage response = await http.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string error = await response.Content.ReadAsStringAsync();
                        var issue = JsonConvert.DeserializeObject<Resources.ODataError>(error);
                        return onFailed(response.StatusCode, response.ReasonPhrase, issue);
                    }
                    var contents = await response.Content.ReadAsStringAsync();
                    return onSuccess(contents);
                }
            }
        }

        public async Task<TResult> SendGraphGetRequest<TResult>(string api, string query,
            Func<string, TResult> onSuccess,
            Func<HttpStatusCode, string, Resources.ODataError, TResult> onFailed)
        {
            string url = Globals.aadGraphEndpoint + tenant + api + "?" + Globals.aadGraphVersion;
            if (!string.IsNullOrEmpty(query))
            {
                url += "&" + query;
            }

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                using (HttpResponseMessage response = await http.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string error = await response.Content.ReadAsStringAsync();
                        var issue = JsonConvert.DeserializeObject<Resources.ODataError>(error);
                        return onFailed(response.StatusCode, response.ReasonPhrase, issue);
                    }
                    var contents = await response.Content.ReadAsStringAsync();
                    return onSuccess(contents);
                }
            }
        }
    }
}
