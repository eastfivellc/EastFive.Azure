using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Security.CredentialProvider.ImplicitCreation
{
    internal class CredentialsDocument : TableEntity
    {
        public string AccessToken { get; set; }
    }
}
