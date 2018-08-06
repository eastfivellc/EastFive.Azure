﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;

using BlackBarLabs;
using BlackBarLabs.Api;
using EastFive.Api.Services;
using BlackBarLabs.Extensions;
using EastFive.Security.CredentialProvider.AzureADB2C;
using EastFive.Extensions;

namespace EastFive.Security.SessionServer.Api.Controllers
{
    public class OpenIdConnectResult
    {
        public string id_token { get; set; }

        public string state { get; set; }

        public string error { get; set; }

        public string error_description { get; set; }
    }

    [RoutePrefix("aadb2c")]
    public class OpenIdResponseController : ResponseController
    {
        public override async Task<IHttpActionResult> Get([FromUri]ResponseResult result)
        {
            if (result.IsDefault())
                result = new ResponseResult();
            result.method = CredentialValidationMethodTypes.Password;
            return await base.Get(result);

            //if (null == result)
            //    return this.Request
            //        .CreateResponseValidationFailure(result, r => r.id_token)
            //        .ToActionResult();

            //if(!Guid.TryParse(result.state, out Guid authenticationRequestId))
            //    return this.Request
            //                    .CreateResponse(HttpStatusCode.Conflict)
            //                    .AddReason("State value is invalid")
            //                    .ToActionResult();

            //var context = Request.GetSessionServerContext();
            //var callbackUrl = this.Url.GetLocation<OpenIdResponseController>();
            //return await context.Sessions.GetAsync(authenticationRequestId,
            //                    callbackUrl,
            //                    (authenticationRequest) =>
            //                    {
            //                        return Redirect(authenticationRequest.loginUrl);
            //                    },
            //                    () =>
            //                    {
            //                        return this.Request
            //                            .CreateResponse(HttpStatusCode.Conflict)
            //                            .AddReason("The login token is no longer valid")
            //                            .ToActionResult();
            //                    },
            //                    (why) =>
            //                    {
            //                        return this.Request
            //                            .CreateResponse(HttpStatusCode.Conflict)
            //                            .AddReason(why)
            //                            .ToActionResult();
            //                    });
        }
        

        //private async Task<IHttpActionResult> CreateResponse(Context context, Guid sessionId, Guid? authorizationId, string jwtToken, string refreshToken,
        //    AuthenticationActions action, IDictionary<string, string> extraParams, Uri redirectUri)
        //{
        //    // Enforce a redirect parameter here since OpenIDCreates on in the state data.
        //    //if (!extraParams.ContainsKey(SessionServer.Configuration.AuthorizationParameters.RedirectUri))
        //    //    return Request.CreateResponse(HttpStatusCode.Conflict).AddReason("Redirect URL not in response parameters").ToActionResult();

        //    var config = Library.configurationManager;
        //    var redirectResponse = await config.GetRedirectUriAsync(context, CredentialValidationMethodTypes.Password, action,
        //        sessionId, authorizationId, jwtToken, refreshToken, extraParams, redirectUri,
        //        (redirectUrl) => Redirect(redirectUrl),
        //        (paramName, why) => Request.CreateResponse(HttpStatusCode.BadRequest).AddReason(why).ToActionResult(),
        //        (why) => Request.CreateResponse(HttpStatusCode.BadRequest).AddReason(why).ToActionResult());
        //    return redirectResponse;
        //}

        public override async Task<IHttpActionResult> Post([FromUri]ResponseResult result)
        {
            if (result.IsDefault())
                result = new ResponseResult();
            result.method = CredentialValidationMethodTypes.Password;
            return await base.Post(result);

            //if (!String.IsNullOrWhiteSpace(result.error))
            //    return this.Request.CreateResponse(HttpStatusCode.Conflict)
            //        .AddReason(result.error_description)
            //        .ToActionResult();

            //var context = this.Request.GetSessionServerContext();
            //var response = await await context.Sessions.AuthenticateAsync<Task<IHttpActionResult>>(Guid.NewGuid(),
            //    CredentialValidationMethodTypes.Password,
            //    new Dictionary<string, string>()
            //    {
            //        { AzureADB2CProvider.StateKey, result.state },
            //        { AzureADB2CProvider.IdTokenKey, result.id_token }
            //    },
            //    (sessionId, authorizationId, jwtToken, refreshToken, action, extraParams, redirectUri) =>
            //    {
            //        return CreateResponse(context, sessionId, authorizationId, jwtToken, refreshToken, action, extraParams, redirectUri);
            //    },
            //    (why) => this.Request.CreateResponse(HttpStatusCode.BadRequest)
            //        .AddReason($"Invalid token:{why}")
            //        .ToActionResult()
            //        .ToTask(),
            //    () => this.Request.CreateResponse(HttpStatusCode.Conflict)
            //        .AddReason($"Token is for user is not connected to a user in this system")
            //        .ToActionResult()
            //        .ToTask(),
            //    (why) => this.Request.CreateResponse(HttpStatusCode.ServiceUnavailable)
            //        .AddReason(why)
            //        .ToActionResult()
            //        .ToTask(),
            //    (why) => this.Request.CreateResponse(HttpStatusCode.InternalServerError)
            //        .AddReason(why)
            //        .ToActionResult()
            //        .ToTask(),
            //    (why) => this.Request.CreateResponse(HttpStatusCode.Conflict)
            //        .AddReason(why)
            //        .ToActionResult()
            //        .ToTask());

            //return response;
        }
    }
}
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
