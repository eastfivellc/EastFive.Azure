using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using BlackBarLabs.Extensions;
using System.Collections.Generic;
using BlackBarLabs;
using EastFive.Security.SessionServer.Persistence.Documents;

namespace EastFive.Security.SessionServer
{
    //public struct AuthorizationCredential
    //{
    //    public string userId;
    //    public string secret;
    //    public bool isEmail;
    //}

    public struct Claim
    {
        public Guid claimId;
        public string issuer;
        public string type;
        public string value;
    }

    //public class Authorizations
    //{
    //    private Context context;
    //    private Persistence.DataContext dataContext;

    //    internal Authorizations(Context context, Persistence.DataContext dataContext)
    //    {
    //        this.dataContext = dataContext;
    //        this.context = context;
    //    }
        
    //    #region Credentials
        
    //    public async Task<TResult> UpdateCredentialsAsync<TResult>(Guid authorizationId,
    //        string username, bool isEmail, string token, bool forceChange,
    //        Func<TResult> success, 
    //        Func<TResult> authorizationDoesNotExists,
    //        Func<string, TResult> updateFailed)
    //    {
    //        throw new NotImplementedException();
    //        ////Updates the Credential Password
    //        //var provider = this.context.GetCredentialProvider(method);
    //        //var result = await await provider.UpdateTokenAsync(providerId, username, token,
    //        //    (returnToken) => Task.FromResult(success()),
    //        //    async () =>
    //        //    {
    //        //        if (method != CredentialValidationMethodTypes.Implicit)
    //        //            return authorizationDoesNotExists();
    //        //        return await CreateCredentialsAsync(authorizationId, 
    //        //            method, providerId, username, token,
    //        //            success,
    //        //            updateFailed,
    //        //            authorizationDoesNotExists,
    //        //            (authIdExists) => updateFailed("already exists"));
    //        //    },
    //        //    () => Task.FromResult(updateFailed("failure")));
    //        //return result;
    //    }

 
        //#endregion

    //}
}
