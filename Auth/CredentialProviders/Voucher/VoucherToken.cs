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
        [ResourceIdentifier]
        public IRef<VoucherToken> voucherTokenRef;

        [LastModified]
        public DateTime lastModified;

        public const string KeySignaturePropertyName = "key_signature";
        [Storage(Name = KeySignaturePropertyName)]
        [ApiProperty(PropertyName = KeySignaturePropertyName)]
        [JsonProperty(PropertyName = KeySignaturePropertyName)]
        [ResourceTitle]
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
        [StorageQuery]
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

        [EastFive.Api.Meta.Flows.WorkflowStep(
            StepName = "Get Voucher Token",
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Version = Workflows.AuthorizationFlow.Version,
            Step = 5.0)]
        [Api.HttpPost]
        public async static Task<IHttpResponse> CreateAsync(
                [Api.Meta.Flows.WorkflowNewId]
                [Property(Name = IdPropertyName)]IRef<VoucherToken> voucherTokenRef,
                
                [Api.Meta.Flows.WorkflowParameter(Value = "{{XAuthorization}}")]
                [PropertyOptional(Name = AuthIdPropertyName)]Guid? authorizationIdMaybe,

                [Api.Meta.Flows.WorkflowParameter(Value = "PFJTQ...1ZT4=", Description = "Found in Appsettings as EastFive.Security.Token.Key")]
                [Property(Name = KeyPropertyName)] string key,

                [Api.Meta.Flows.WorkflowParameter(Value = "{{$randomDateFuture}}")]
                [Property(Name = ExpirationPropertyName)] DateTime expiration,

                [Api.Meta.Flows.WorkflowObjectParameter(
                    Key0 ="http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
                    Value0 = ClaimValues.RoleType + "fb7f557f458c4eadb08652c4a7315fd6",
                    Key1 = ClaimValues.AccountType,
                    Value1 = "{{Account}}")]
                [Property(Name = ClaimsPropertyName)] Dictionary<string, string> extraClaims,

                [Resource] VoucherToken voucherToken,

            [Api.Meta.Flows.WorkflowVariable(Workflows.AuthorizationFlow.Variables.ApiVoucher, TokenPropertyName)]
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

                    voucherToken.claims = voucherToken.claims
                            .NullToEmpty()
                            .Concat(extraClaims.NullToEmpty())
                            .Distinct(kvp => kvp.Key)
                            .ToDictionary();

                    return voucherToken.StorageCreateAsync(
                        createdId => onCreated(voucherToken),
                        () => onAlreadyExists());
                },
                (why) => onFailure(why).AsTask());
        }

        [HttpAction("whoami")]
        [ApiKeyAccess]
        [SuperAdminClaim]
        public static IHttpResponse WhoAmI(
                EastFive.Api.Security security,
            ContentTypeResponse<Guid> onFound)
        {
            return onFound(security.performingAsActorId);
        }

        #endregion


    }
}
