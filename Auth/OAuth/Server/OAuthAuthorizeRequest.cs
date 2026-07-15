using System;
using Newtonsoft.Json;

using EastFive;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

namespace EastFive.Azure.OAuth.Server
{
    /// <summary>
    /// A pending request to the /oauth/authorize endpoint (RFC 6749 §4.1.1),
    /// persisted across the login-provider redirect hop and the consent screen.
    /// </summary>
    [StorageTable]
    public struct OAuthAuthorizeRequest : IReferenceable
    {
        public const string StatusPending = "pending";
        public const string StatusConsentPending = "consent-pending";
        public const string StatusConsumed = "consumed";

        [JsonIgnore]
        public Guid id => @ref.id;

        [RowKey]
        [RowKeyPrefix(Characters = 3)]
        [JsonIgnore]
        public IRef<OAuthAuthorizeRequest> @ref;

        [ETag]
        [JsonIgnore]
        public string eTag;

        [Storage]
        public string clientId;

        [Storage]
        public string redirectUri;

        [Storage]
        public string state;

        [Storage]
        public string scope;

        [Storage]
        public string codeChallenge;

        [Storage]
        public string codeChallengeMethod;

        /// <summary>RFC 8707 resource indicator (audience the client wants the token for).</summary>
        [Storage]
        public string resource;

        /// <summary>The EastFive login-flow authorization used to authenticate the user.</summary>
        [Storage]
        public IRefOptional<EastFive.Azure.Auth.Authorization> authorization;

        [Storage]
        public Guid? accountId;

        /// <summary>One-time key embedded in the consent form to bind the approval POST to this request.</summary>
        [Storage]
        public string approvalKey;

        [Storage]
        public string status;

        [Storage]
        public DateTime createdOn;
    }
}
