using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EastFive.Azure.Auth.CredentialProviders
{
    public interface IProvideToken : IProvideAuthorization
    {
        IDictionary<string, string> CreateTokens(Guid actorId);
    }
}
