using EastFive.Api;
using EastFive.Api.Controllers;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Login
{
    [FunctionViewController(
        Route = "LoginClient",
        ContentType = "x-application/login-client",
        Namespace = "e5/login",
        ContentTypeVersion = "0.1")]
    public struct Client : IReferenceable
    {
        [JsonIgnore]
        public Guid id => clientRef.id;

        public const string ClientPropertyName = "id";
        [ApiProperty(PropertyName = ClientPropertyName)]
        [JsonProperty(PropertyName = ClientPropertyName)]
        [RowKey]
        [StandardParititionKey]
        public IRef<Client> clientRef;

        public const string SecretPropertyName = "secret";
        [ApiProperty(PropertyName = SecretPropertyName)]
        [JsonProperty(PropertyName = SecretPropertyName)]
        [Storage]
        public string secret;

        [Unsecured("OAuth client registration endpoint - allows external systems to register OAuth client credentials")]
        [Api.HttpPost]
        public static async Task<IHttpResponse> CreateAsync(
                [Resource]Client client,
            CreatedResponse onCreated,
            AlreadyExistsResponse onAlreadyExists,
            GeneralConflictResponse onFailure)
        {
            return await client
                .StorageCreateAsync(
                    (discard) =>
                    {
                        return onCreated();
                    },
                    () => onAlreadyExists());
        }

        [Unsecured("OAuth client listing endpoint - returns registered OAuth clients for configuration purposes")]
        [Api.HttpGet]
        public static IHttpResponse ListAsync(
            MultipartAsyncResponse<Client> onListed)
        {
            return typeof(Client)
                .StorageGetAll()
                .Select(c => (Client)c)
                .HttpResponse(onListed);;
        }
    }
}
