using BlackBarLabs.Persistence.Azure.Attributes;
using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Security;
using EastFive.Serialization;
using EastFive.Web.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Auth.CredentialProviders.Voucher
{
    [FunctionViewController(
        Route = "VoucherToken",
        ContentType = "x-application/auth-voucher-token",
        ContentTypeVersion = "0.1")]
    [StorageTable]

    public class VoucherToken : IReferenceable
    {
        #region Properties

        [JsonIgnore]
        public Guid id => voucherTokenRef.id;

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [StandardParititionKey]
        public IRef<VoucherToken> voucherTokenRef;

        [LastModified]
        public DateTime lastModified;

        public const string KeySignaturePropertyName = "key_signature";
        [Storage(Name = KeySignaturePropertyName)]
        [ApiProperty(PropertyName = KeySignaturePropertyName)]
        [JsonProperty(PropertyName = KeySignaturePropertyName)]
        public string keySignature { get; set; }

        public const string DescriptionPropertyName = "description";
        [Storage]
        [ApiProperty(PropertyName = DescriptionPropertyName)]
        [JsonProperty(PropertyName = DescriptionPropertyName)]
        public string description { get; set; }

        public const string KeyPropertyName = "key";
        [ApiProperty(PropertyName = KeyPropertyName)]
        [JsonProperty(PropertyName = KeyPropertyName)]
        public string key { get; set; }

        public const string AuthIdPropertyName = "auth_id";
        [Storage(Name = AuthIdPropertyName)]
        [ApiProperty(PropertyName = AuthIdPropertyName)]
        [JsonProperty(PropertyName = AuthIdPropertyName)]
        public Guid authId { get; set; }

        public const string TokenPropertyName = "token";
        [ApiProperty(PropertyName = TokenPropertyName)]
        [JsonProperty(PropertyName = TokenPropertyName)]
        public string token { get; set; }

        public const string ExpirationPropertyName = "expiration";
        [Storage(Name = ExpirationPropertyName)]
        [ApiProperty(PropertyName = ExpirationPropertyName)]
        [JsonProperty(PropertyName = ExpirationPropertyName)]
        public DateTime expiration { get; set; }

        public const string ClaimsPropertyName = "claims";
        [Storage(Name = ClaimsPropertyName)]
        [ApiProperty(PropertyName = ClaimsPropertyName)]
        [JsonProperty(PropertyName = ClaimsPropertyName)]
        public Dictionary<string, string> claims { get; set; }

        #endregion

        #region Http Methods

        [Api.HttpPost]
        public async static Task<IHttpResponse> CreateAsync(
                [Property(Name = IdPropertyName)]IRef<VoucherToken> voucherTokenRef,
                [PropertyOptional(Name = AuthIdPropertyName)]Guid? authorizationIdMaybe,
                [Property(Name = KeyPropertyName)] string key,
                [Property(Name = ExpirationPropertyName)] DateTime expiration,
                [Resource] VoucherToken voucherToken,
            CreatedBodyResponse<VoucherToken> onCreated,
            AlreadyExistsResponse onAlreadyExists,
            GeneralConflictResponse onFailure)
        {
            return await VoucherTools.GenerateUrlToken(voucherTokenRef.id, expiration,
                    key,
                token =>
                {
                    voucherToken.keySignature = key.MD5HashGuid().ToString("N");
                    voucherToken.token = token;
                    voucherToken.key = default; // Don't save or return the key

                    if (authorizationIdMaybe.HasValue)
                    {
                        voucherToken.claims = voucherToken.claims
                            .NullToEmpty()
                            .Append(Api.AppSettings.ActorIdClaimType.ConfigurationString(
                                (accountIdClaimType) => accountIdClaimType.PairWithValue(authorizationIdMaybe.Value.ToString("N"))))
                            .Distinct(kvp => kvp.Key)
                            .ToDictionary();
                    }
                    return voucherToken.StorageCreateAsync(
                        createdId => onCreated(voucherToken),
                        () => onAlreadyExists());
                },
                (why) => onFailure(why).AsTask());
        }

        [HttpAction("whoami")]
        [ApiKeyAccess]
        [RequiredClaim(
            System.Security.Claims.ClaimTypes.Role,
            ClaimValues.Roles.SuperAdmin)]
        public static IHttpResponse WhoAmI(
                EastFive.Api.Security security,
            ContentTypeResponse<Guid> onFound)
        {
            return onFound(security.performingAsActorId);
        }

        #endregion


    }
}
