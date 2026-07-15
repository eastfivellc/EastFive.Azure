using System;
using Newtonsoft.Json;

using EastFive;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

namespace EastFive.Azure.OAuth.Server
{
    /// <summary>
    /// Rotating refresh token (OAuth 2.1 §4.3.1). Row id = OAuthServer.ComputeLookupGuid(token).
    /// On rotation the old record is revoked and points at its replacement (replacedBy) so that
    /// reuse of a rotated token can revoke the whole descendant chain (reuse detection).
    /// </summary>
    [StorageTable]
    public struct OAuthRefreshTokenRecord : IReferenceable
    {
        [JsonIgnore]
        public Guid id => @ref.id;

        [RowKey]
        [RowKeyPrefix(Characters = 3)]
        [JsonIgnore]
        public IRef<OAuthRefreshTokenRecord> @ref;

        [ETag]
        [JsonIgnore]
        public string eTag;

        [Storage]
        public string tokenHash;

        [Storage]
        public string clientId;

        [Storage]
        public string scope;

        [Storage]
        public string resource;

        [Storage]
        public IRefOptional<EastFive.Azure.Auth.Authorization> authorization;

        [Storage]
        public Guid accountId;

        /// <summary>Stable across rotations of the same original grant.</summary>
        [Storage]
        public Guid familyId;

        /// <summary>The persisted <see cref="EastFive.Azure.Auth.Session"/> anchoring this grant
        /// (endpoints like Whoami resolve the session row from the token's session claim).</summary>
        [Storage]
        public Guid sessionId;

        [Storage]
        public DateTime expiresOn;

        [Storage]
        public bool revoked;

        /// <summary>Set at rotation: the record that superseded this one.</summary>
        [Storage]
        public IRefOptional<OAuthRefreshTokenRecord> replacedBy;
    }
}
