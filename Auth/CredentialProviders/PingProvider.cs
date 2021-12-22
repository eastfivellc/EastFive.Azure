using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System.Security.Claims;
using System.Net.Http;
using System.Net.Http.Headers;

using Newtonsoft.Json;

using EastFive.Serialization;
using EastFive.Azure.Auth;
using EastFive.Extensions;

namespace EastFive.Azure.Auth.CredentialProviders
{
    [IntegrationName(PingProvider.IntegrationName)]
    public class PingProvider : IProvideLogin
    {
        public const string IntegrationName = "Ping";
        public string Method => IntegrationName;
        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

        public const string TokenId = "tokenid";
        public const string AgentId = "agentid";
        public const string RestApiKey = "PingAuthName";
        public const string Subject = "pingone.subject";
        public const string LastName = "lastName";
        public const string FirstName = "firstName";
        public const string Email = "email";
        public const string PracticeId = "practiceID";
        public const string DepartmentId = "departmentID";
        public const string PatientId = "patientID";
        public const string EncounterId = "extraidentifier";
        public const string ReportSetId = "ReportSetId";

        public PingProvider()
        {
        }
        
        private static string GetTokenServiceUrl(string pingConnectToken)
        {
            return $"https://sso.connect.pingidentity.com/sso/TXS/2.0/1/{pingConnectToken}";
            //return "https://sso.connect.pingidentity.com/sso/TXS/2.0/2/" + pingConnectToken;
        }

        [IntegrationName(PingProvider.IntegrationName)]
        public static Task<TResult> InitializeAsync<TResult>(
            Func<IProvideAuthorization, TResult> onProvideAuthorization,
            Func<TResult> onProvideNothing,
            Func<string, TResult> onFailure)
        {
            return onProvideAuthorization(new PingProvider()).AsTask();
        }

        public Type CallbackController => typeof(PingResponse);

        public virtual async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> extraParams,
            Func<string, Guid?, Guid?, IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            if (!extraParams.ContainsKey(PingProvider.TokenId))
                return onInvalidCredentials("Token Id was not provided");
            if (!extraParams.ContainsKey(PingProvider.AgentId))
                return onInvalidCredentials("AgentId was not provided");
            var tokenId = extraParams[PingProvider.TokenId];
            var agentId = extraParams[PingProvider.AgentId];
            var restAuthUsername = extraParams[PingProvider.RestApiKey];

            return await Web.Configuration.Settings.GetString<Task<TResult>>(EastFive.Azure.AppSettings.Auth.Ping.PingIdentityAthenaRestApiKey,
                async (restApiKey) =>
                {
                    using (var httpClient = new HttpClient())
                    {
                        var credentials = Encoding.ASCII.GetBytes($"{restAuthUsername}:{restApiKey}");
                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentials));
                        var tokenUrl = GetTokenServiceUrl(tokenId);
                        using (var request = new HttpRequestMessage(
                            new HttpMethod("GET"), tokenUrl))
                        {
                            request.Headers.Add("Cookie", "agentid=" + agentId);
                            try
                            {
                                using (var response = await httpClient.SendAsync(request))
                                {
                                    var content = await response.Content.ReadAsStringAsync();
                                    if (response.StatusCode == HttpStatusCode.OK)
                                    {
                                        dynamic stuff = null;
                                        try
                                        {
                                            stuff = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(content);
                                        }
                                        catch (Newtonsoft.Json.JsonReaderException)
                                        {
                                            return onCouldNotConnect($"PING Returned non-json response:{content}");
                                        }
                                        string subject = (string)stuff[Subject];
                                        var loginId = Guid.NewGuid();
                                        var extraParamsWithTokenValues = new Dictionary<string, string>(extraParams);
                                        foreach (var item in stuff)
                                        {
                                            extraParamsWithTokenValues.Add(item.Key.ToString(), item.Value.ToString());
                                        }

                                        void ShimKey(string expectedName)
                                        {
                                            var alternateName = expectedName.ToLower();
                                            if (!extraParamsWithTokenValues.ContainsKey(expectedName) && extraParamsWithTokenValues.TryGetValue(alternateName.ToLower(), out string value))
                                            {
                                                extraParamsWithTokenValues.Add(expectedName, value);
                                                extraParamsWithTokenValues.Remove(alternateName);
                                            }
                                        }

                                        // shim differences in casing of keys among connection setups
                                        ShimKey(PracticeId);
                                        ShimKey(DepartmentId);
                                        ShimKey(PatientId);

                                        return onSuccess(subject, default(Guid?), loginId, extraParamsWithTokenValues);
                                    }
                                    else
                                    {
                                        return onFailure($"{content} TokenId: {tokenId}, AgentId: {agentId}");
                                    }
                                }
                            }
                            catch (System.Net.Http.HttpRequestException ex)
                            {
                                return onCouldNotConnect($"{ex.GetType().FullName}:{ex.Message}");
                            }
                            catch (Exception exGeneral)
                            {
                                return onCouldNotConnect(exGeneral.Message);
                            }
                        }
                    }
                },
                (why) => onUnspecifiedConfiguration(why).AsTask());
        }
        
        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> responseParams, 
            Func<string, Guid?, Guid?, TResult> onSuccess, 
            Func<string, TResult> onFailure)
        {
            if (!responseParams.ContainsKey(Subject))
                return onFailure("Missing pingone.subject");

            string subject = responseParams[Subject];
            using (var algorithm = SHA512.Create())
            {
                var hash = algorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(subject));
                var loginId = new Guid(hash.Take(16).ToArray());

                return onSuccess(subject, default(Guid?), loginId);
            }
        }

        #region IProvideLogin

        public Uri GetLogoutUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return default(Uri);
        }

        public Uri GetLoginUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return default(Uri);
        }

        public Uri GetSignupUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return default(Uri);
        }

        public Task<TResult> UserParametersAsync<TResult>(Guid actorId, System.Security.Claims.Claim[] claims, 
                IDictionary<string, string> extraParams, 
            Func<IDictionary<string, string>, IDictionary<string, Type>, IDictionary<string, string>, TResult> onSuccess)
        {
            return onSuccess(
                new Dictionary<string, string>() { { "push_pmp_file_to_ehr", "Push PMP file to EHR" } },
                new Dictionary<string, Type>() { { "push_pmp_file_to_ehr", typeof(bool) } },
                new Dictionary<string, string>() { { "push_pmp_file_to_ehr", "When true, the system will push PMP files into the provider's clinical documents in their EHR system." } }).AsTask();
        }

        #endregion

    }
}
