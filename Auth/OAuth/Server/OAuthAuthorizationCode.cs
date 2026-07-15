using System;
using Newtonsoft.Json;

using EastFive;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

namespace EastFive.Azure.OAuth.Server
{
    /// <summary>
    /// Single-use authorization code (RFC 6749 §4.1.2 / OAuth 2.1).
    /// Row id = OAuthServer.ComputeLookupGuid(code); the code itself is never stored,
    /// only its full SHA256 (codeHash) for verification.
    /// </summary>
    [StorageTable]
    public struct OAuthAuthorizationCode : IReferenceable
    {
        [JsonIgnore]
        public Guid id => @ref.id;

        [RowKey]
        [RowKeyPrefix(Characters = 3)]
        [JsonIgnore]
        public IRef<OAuthAuthorizationCode> @ref;

        [ETag]
        [JsonIgnore]
        public string eTag;

        [Storage]
        public string codeHash;

        [Storage]
        public string clientId;

        [Storage]
        public string redirectUri;

        [Storage]
        public string scope;

        [Storage]
        public string codeChallenge;

        [Storage]
        public string resource;

        [Storage]
        public IRefOptional<EastFive.Azure.Auth.Authorization> authorization;

        [Storage]
        public Guid accountId;

        [Storage]
        public DateTime expiresOn;

        [Storage]
        public bool consumed;
    }
}
