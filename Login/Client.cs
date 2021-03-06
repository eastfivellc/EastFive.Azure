﻿using EastFive.Api;
using EastFive.Api.Controllers;
using EastFive.Azure.Persistence.AzureStorageTables;
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
    [FunctionViewController6(
        Route = "LoginClient",
        Resource = typeof(Client),
        ContentType = "x-application/login-client",
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

        [Api.HttpPost]
        public static async Task<HttpResponseMessage> CreateAsync(
                [Resource]Client client,
                Api.Azure.AzureApplication application,
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
    }
}
