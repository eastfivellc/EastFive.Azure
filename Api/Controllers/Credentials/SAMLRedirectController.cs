//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net;
//using System.Net.Http;
//using System.Threading.Tasks;
//using System.Web;
//using System.Web.Http;

//using BlackBarLabs;
//using BlackBarLabs.Api;
//using EastFive.Api.Services;
//using System.Xml;
//using System.Xml.Linq;
//using Newtonsoft.Json;
//using System.Dynamic;
//using EastFive.Collections.Generic;
//using EastFive.Linq;
//using EastFive.Api.Azure.Controllers;
//using EastFive.Security.SessionServer;
//using EastFive.Extensions;
//using System.IO;

//namespace EastFive.Api.Azure.Credentials.Controllers
//{
//    // aadb2c/SAMLRedirect?tokenid=ID7ee36a406286079a7237b23dd7647d95b8d42ddbcde4fbe8030000015d5790a407&agentid=e924bba8
//    [RoutePrefix("aadb2c")]
//    public class SAMLRedirectController : BaseController
//    {
//        public async Task<IHttpActionResult> Post()
//        {
//            return await await this.Request.Content.ParseMultipartAsync(
//                (byte[] SAMLResponse) => ParseSAMLResponseAsync(SAMLResponse),
//                () => this.Request
//                    .CreateResponse(HttpStatusCode.BadRequest)
//                    .AddReason("Content was not multipart")
//                    .ToActionResult()
//                    .AsTask());
//        }

//        private TResult ParseToDictionary<TResult>(string samlResponse,
//            Func<IDictionary<string, string>, TResult> onSuccess,
//            Func<string, TResult> onFailure)
//        {
//            try
//            {
//                var settings = new XmlReaderSettings
//                {
//                    DtdProcessing = DtdProcessing.Ignore, // prevents XXE attacks, such as Billion Laughs
//                    MaxCharactersFromEntities = 1024,
//                    XmlResolver = null,                   // prevents external entity DoS attacks, such as slow loading links or large file requests
//                };
//                using (var strReader = new StringReader(samlResponse))
//                using (var xmlReader = XmlReader.Create(strReader, settings))
//                {
//                    var doc = XDocument.Load(xmlReader);
//                    string jsonText = JsonConvert.SerializeXNode(doc);
//                    var dyn = JsonConvert.DeserializeObject<ExpandoObject>(jsonText);

//                    var response = ((IDictionary<string, object>)dyn)[SAMLProvider.SamlpResponseKey];
//                    var assertion = (IDictionary<string, object>)(((IDictionary<string, object>)response)[SAMLProvider.SamlAssertionKey]);
//                    var subject = assertion[SAMLProvider.SamlSubjectKey];
//                    var nameIdNode = ((IDictionary<string, object>)subject)[SAMLProvider.SamlNameIDKey];
//                    var nameId = (string)((IDictionary<string, object>)nameIdNode)["#text"];
//                    return onSuccess(
//                        assertion
//                            .Select(kvp => kvp.Key.PairWithValue(kvp.Value.ToString()))
//                            .Append(SAMLProvider.SamlNameIDKey.PairWithValue(nameId))
//                            .ToDictionary());
//                }
//            }
//            catch (Exception ex)
//            {
//                return onFailure(ex.Message);
//            }
//        }

//        private async Task<IHttpActionResult> ParseSAMLResponseAsync(byte[] samlResponseBytes)
//        {
//            var samlResponse = System.Text.Encoding.UTF8.GetString(samlResponseBytes);
//            if (String.IsNullOrWhiteSpace(samlResponse))
//                return this.Request.CreateResponse(HttpStatusCode.Conflict)
//                            .AddReason("SAML Response not provided in form POST")
//                            .ToActionResult();

//            return await ParseToDictionary(samlResponse,
//                async (tokens) =>
//                {
//                    var context = Request.GetSessionServerContext();
//                    return await await context.Sessions.UpdateWithAuthenticationAsync<Task<IHttpActionResult>>(Guid.NewGuid(),
//                        null, Enum.GetName(typeof(CredentialValidationMethodTypes), CredentialValidationMethodTypes.SAML), tokens,
//                        (sessionId, authorizationId, token, refreshToken, action, extraParams, redirectUri) =>
//                        {
//                            var config = Library.configurationManager;
//                            var redirectResponse = config.GetRedirectUriAsync<IHttpActionResult>(context,
//                                Enum.GetName(typeof(CredentialValidationMethodTypes), CredentialValidationMethodTypes.SAML), action,
//                                sessionId, authorizationId, token, refreshToken, extraParams, redirectUri,
//                                (redirectUrl) => Request.CreateRedirectResponse(redirectUrl).ToActionResult(),
//                                (paramName, why) => Request.CreateResponse(HttpStatusCode.BadRequest).AddReason(why).ToActionResult(),
//                                (why) => Request.CreateResponse(HttpStatusCode.BadRequest).AddReason(why).ToActionResult());
//                            return redirectResponse;
//                        },
//                        (location, why, paramsExtra) => Request.CreateRedirectResponse(location)
//                                    .AddReason(why)
//                                    .ToActionResult()
//                                    .AsTask(),
//                        (why) => this.Request.CreateResponse(HttpStatusCode.BadRequest)
//                                    .AddReason($"Invalid token:{why}")
//                                    .ToActionResult()
//                                    .AsTask(),
//                        () => this.Request.CreateResponse(HttpStatusCode.Conflict)
//                                    .AddReason($"Token is not connected to a user in this system")
//                                    .ToActionResult()
//                                    .AsTask(),
//                        (why) => this.Request.CreateResponse(HttpStatusCode.ServiceUnavailable)
//                                    .AddReason(why)
//                                    .ToActionResult()
//                                    .AsTask(),
//                        (why) => this.Request.CreateResponse(HttpStatusCode.InternalServerError)
//                                    .AddReason(why)
//                                    .ToActionResult()
//                                    .AsTask(),
//                        (why) => this.Request.CreateResponse(HttpStatusCode.Conflict)
//                                    .AddReason(why)
//                                    .ToActionResult()
//                                    .AsTask());
//                        },
//                        (why) => Request.CreateResponse(HttpStatusCode.BadRequest)
//                            .AddReason(why)
//                            .ToActionResult()
//                            .AsTask());
            
//        }
//    }
//}