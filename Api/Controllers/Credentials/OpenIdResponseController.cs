using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using EastFive;
using EastFive.Linq;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Collections.Generic;

using Newtonsoft.Json;

namespace EastFive.Azure.Auth
{
    [FunctionViewController6(
        Route = "OpenIdResponse",
        Resource = typeof(OpenIdResponse),
        ContentType = "x-application/open-id-response",
        ContentTypeVersion = "0.1")]
    public class OpenIdResponse : EastFive.Azure.Auth.Redirection
    {
        public const string StatePropertyName = "state";
        [ApiProperty(PropertyName = StatePropertyName)]
        [JsonProperty(PropertyName = StatePropertyName)]
        public string state { get; set; }

        public const string CodePropertyName = "code";
        [ApiProperty(PropertyName = CodePropertyName)]
        [JsonProperty(PropertyName = CodePropertyName)]
        public string code { get; set; }

        public const string TokenPropertyName = "id_token";
        [ApiProperty(PropertyName = TokenPropertyName)]
        [JsonProperty(PropertyName = TokenPropertyName)]
        public string token { get; set; }

        public const string ErrorPropertyName = "error";
        [ApiProperty(PropertyName = ErrorPropertyName)]
        [JsonProperty(PropertyName = ErrorPropertyName)]
        public string error { get; set; }

        public const string ErrorDescriptionPropertyName = "error_description";
        [ApiProperty(PropertyName = ErrorDescriptionPropertyName)]
        [JsonProperty(PropertyName = ErrorDescriptionPropertyName)]
        public string errorDescription { get; set; }

        [HttpPost(MatchAllParameters = false)]
        public static async Task<HttpResponseMessage> Post(
                [Property(Name = StatePropertyName)]string state,
                [PropertyOptional(Name = CodePropertyName)]string code,
                [Property(Name = TokenPropertyName)]string token,
                AzureApplication application,
                HttpRequestMessage request,
                IInvokeApplication urlHelper,
            RedirectResponse onRedirectResponse,
            BadRequestResponse onBadCredentials,
            HtmlResponse onCouldNotConnect,
            HtmlResponse onGeneralFailure)
        {
            var method = await EastFive.Azure.Auth.Method.ByMethodName(
                EastFive.Api.Azure.Credentials.AzureADB2CProvider.IntegrationName, application);
            var requestParams = request
                .GetQueryNameValuePairs()
                .Distinct(kvp => kvp.Key)
                .ToDictionary();
            if (state.HasBlackSpace())
                requestParams.Add(StatePropertyName, state);
            if (code.HasBlackSpace())
                requestParams.Add(CodePropertyName, code);
            if (token.HasBlackSpace())
                requestParams.Add(TokenPropertyName, token);

            return await ProcessRequestAsync(method,
                    requestParams,
                    application, request, urlHelper,
                (redirect, accountIdMaybe) =>
                {
                    return onRedirectResponse(redirect);
                },
                (why) => onBadCredentials().AddReason(why),
                (why) =>
                {
                    return onCouldNotConnect(why);
                },
                (why) =>
                {
                    return onGeneralFailure(why);
                });
        }
    }
}


//namespace EastFive.Api.Azure.Credentials.Controllers
//{
//    public class OpenIdConnectResult
//    {
//        public string id_token { get; set; }

//        public string state { get; set; }

//        public string error { get; set; }

//        public string error_description { get; set; }
//    }


//    [RoutePrefix("aadb2c")]
//    public class OpenIdResponseController : ResponseCon
//    {
//        public override async Task<IHttpActionResult> Get([FromUri]ResponseResult result)
//        {
//            if (result.IsDefault())
//                result = new ResponseResult();
//            result.method = CredentialValidationMethodTypes.Password;
//            return await base.Get(result);

//            //if (null == result)
//            //    return this.Request
//            //        .CreateResponseValidationFailure(result, r => r.id_token)
//            //        .ToActionResult();

//            //if(!Guid.TryParse(result.state, out Guid authenticationRequestId))
//            //    return this.Request
//            //                    .CreateResponse(HttpStatusCode.Conflict)
//            //                    .AddReason("State value is invalid")
//            //                    .ToActionResult();

//            //var context = Request.GetSessionServerContext();
//            //var callbackUrl = this.Url.GetLocation<OpenIdResponseController>();
//            //return await context.Sessions.GetAsync(authenticationRequestId,
//            //                    callbackUrl,
//            //                    (authenticationRequest) =>
//            //                    {
//            //                        return Redirect(authenticationRequest.loginUrl);
//            //                    },
//            //                    () =>
//            //                    {
//            //                        return this.Request
//            //                            .CreateResponse(HttpStatusCode.Conflict)
//            //                            .AddReason("The login token is no longer valid")
//            //                            .ToActionResult();
//            //                    },
//            //                    (why) =>
//            //                    {
//            //                        return this.Request
//            //                            .CreateResponse(HttpStatusCode.Conflict)
//            //                            .AddReason(why)
//            //                            .ToActionResult();
//            //                    });
//        }
        

//        //private async Task<IHttpActionResult> CreateResponse(Context context, Guid sessionId, Guid? authorizationId, string jwtToken, string refreshToken,
//        //    AuthenticationActions action, IDictionary<string, string> extraParams, Uri redirectUri)
//        //{
//        //    // Enforce a redirect parameter here since OpenIDCreates on in the state data.
//        //    //if (!extraParams.ContainsKey(SessionServer.Configuration.AuthorizationParameters.RedirectUri))
//        //    //    return Request.CreateResponse(HttpStatusCode.Conflict).AddReason("Redirect URL not in response parameters").ToActionResult();

//        //    var config = Library.configurationManager;
//        //    var redirectResponse = await config.GetRedirectUriAsync(context, CredentialValidationMethodTypes.Password, action,
//        //        sessionId, authorizationId, jwtToken, refreshToken, extraParams, redirectUri,
//        //        (redirectUrl) => Redirect(redirectUrl),
//        //        (paramName, why) => Request.CreateResponse(HttpStatusCode.BadRequest).AddReason(why).ToActionResult(),
//        //        (why) => Request.CreateResponse(HttpStatusCode.BadRequest).AddReason(why).ToActionResult());
//        //    return redirectResponse;
//        //}

//        public override async Task<IHttpActionResult> Post([FromUri]ResponseResult result)
//        {
//            if (result.IsDefault())
//                result = new ResponseResult();
//            result.method = CredentialValidationMethodTypes.Password;
//            return await base.Post(result);

//            //if (!String.IsNullOrWhiteSpace(result.error))
//            //    return this.Request.CreateResponse(HttpStatusCode.Conflict)
//            //        .AddReason(result.error_description)
//            //        .ToActionResult();

//            //var context = this.Request.GetSessionServerContext();
//            //var response = await await context.Sessions.AuthenticateAsync<Task<IHttpActionResult>>(Guid.NewGuid(),
//            //    CredentialValidationMethodTypes.Password,
//            //    new Dictionary<string, string>()
//            //    {
//            //        { AzureADB2CProvider.StateKey, result.state },
//            //        { AzureADB2CProvider.IdTokenKey, result.id_token }
//            //    },
//            //    (sessionId, authorizationId, jwtToken, refreshToken, action, extraParams, redirectUri) =>
//            //    {
//            //        return CreateResponse(context, sessionId, authorizationId, jwtToken, refreshToken, action, extraParams, redirectUri);
//            //    },
//            //    (why) => this.Request.CreateResponse(HttpStatusCode.BadRequest)
//            //        .AddReason($"Invalid token:{why}")
//            //        .ToActionResult()
//            //        .ToTask(),
//            //    () => this.Request.CreateResponse(HttpStatusCode.Conflict)
//            //        .AddReason($"Token is for user is not connected to a user in this system")
//            //        .ToActionResult()
//            //        .ToTask(),
//            //    (why) => this.Request.CreateResponse(HttpStatusCode.ServiceUnavailable)
//            //        .AddReason(why)
//            //        .ToActionResult()
//            //        .ToTask(),
//            //    (why) => this.Request.CreateResponse(HttpStatusCode.InternalServerError)
//            //        .AddReason(why)
//            //        .ToActionResult()
//            //        .ToTask(),
//            //    (why) => this.Request.CreateResponse(HttpStatusCode.Conflict)
//            //        .AddReason(why)
//            //        .ToActionResult()
//            //        .ToTask());

//            //return response;
//        }
//    }
//}
// https://login.microsoftonline.com/humatestlogin.onmicrosoft.com/oauth2/authorize?client_id=bb2a2e3a-c5e7-4f0a-88e0-8e01fd3fc1f4&redirect_uri=https:%2f%2flogin.microsoftonline.com%2fte%2fhumatestlogin.onmicrosoft.com%2foauth2%2fauthresp&response_type=id_token&scope=email+openid&response_mode=query&nonce=ZjJlb75S5AoaET4v6TLuxw%3d%3d&  nux=1&nca=1&domain_hint=humatestlogin.onmicrosoft.com&prompt=login&mkt=en-US&lc=1033&state=eyJTSUQiOiJ4LW1zLWNwaW0tcmM6NzJjNzQ2N2ItYTFiMi00MjdjLThlZTgtZDBmMTM3YjNlZGZkIiwiVElEIjoiNjMzZjdiZTktOTAxNy00ZDFkLWJjNWEtOTBmYWM3MWUxNWU3In0
// https://login.microsoftonline.com/humatestlogin.onmicrosoft.com/oauth2/authorize?client_id=bb2a2e3a-c5e7-4f0a-88e0-8e01fd3fc1f4&redirect_uri=https:%2f%2flogin.microsoftonline.com%2fte%2fhumatestlogin.onmicrosoft.com%2foauth2%2fauthresp&response_type=id_token&scope=email+openid&response_mode=query&nonce=zEu4M5xhVG68UMNVV%2busug%3d%3d&nux=1&nca=1&domain_hint=humatestlogin.onmicrosoft.com&prompt=login&mkt=en-US&lc=1033&state=eyJTSUQiOiJ4LW1zLWNwaW0tcmM6ZWFmMzM0MWMtN2ZlOC00MjAxLWExYjgtN2QxMGEwM2M0MzQxIiwiVElEIjoiNjMzZjdiZTktOTAxNy00ZDFkLWJjNWEtOTBmYWM3MWUxNWU3In0

//https://login.microsoftonline.com/fabrikamb2c.onmicrosoft.com/oauth2/v2.0/authorize?
//client_id=90c0fe63-bcf2-44d5-8fb7-b8bbc0b29dc6
//&response_type=code+id_token
//&redirect_uri=https%3A%2F%2Faadb2cplayground.azurewebsites.net%2F
//&response_mode=form_post
//&scope=openid%20offline_access
//&state=arbitrary_data_you_can_receive_in_the_response
//&nonce=12345
//&p=b2c_1_sign_in
