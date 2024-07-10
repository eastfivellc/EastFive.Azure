using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Api.Meta.Flows;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Security;
using EastFive.Serialization;
using EastFive.Web;
using EastFive.Web.Configuration;
using Microsoft.Azure.Cosmos.Table;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public const string LastModifiedByPropertyName = "last_modified_by";
        [Storage(Name = LastModifiedByPropertyName)]
        [ApiProperty(PropertyName = LastModifiedByPropertyName)]
        [JsonProperty(PropertyName = LastModifiedByPropertyName)]
        public string lastModifiedBy { get; set; }

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
        [StorageQuery]
        [ApiProperty(PropertyName = ExpirationPropertyName)]
        [JsonProperty(PropertyName = ExpirationPropertyName)]
        public DateTime expiration { get; set; }

        public const string ClaimsPropertyName = "claims";
        [Storage(Name = ClaimsPropertyName)]
        [ApiProperty(PropertyName = ClaimsPropertyName)]
        [JsonProperty(PropertyName = ClaimsPropertyName)]
        public Dictionary<string, string> claims { get; set; }

        [JsonIgnore]
        public string DescriptionOrOtherLabel => description.HasBlackSpace()
            ? description
            : !authId.IsDefault()
                ? authId.ToString("N").Substring(6)
                : id.ToString("N").Substring(6);

        #endregion

        #region Http Methods

        public struct SecurityLog : IComparable<SecurityLog>
        {
            public Guid id;
            public string description;
            public string lastModifiedBy;
            public DateTime timestamp;

            public SecurityLog(VoucherToken token)
            {
                id = token.id;
                description = token.description;
                lastModifiedBy = token.lastModifiedBy;
                timestamp = token.lastModified;
            }

            public int CompareTo(SecurityLog other)
            {
                // sort by lastmodified, then description
                if (timestamp == other.timestamp)
                    return description.CompareTo(other.description);

                // descending
                return other.timestamp.CompareTo(timestamp);
            }
        }

        [HttpAction("SecurityLog")]
        [EastFive.Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Version = Workflows.AuthorizationFlow.Version,
            Scope = Workflows.AuthorizationFlow.Scopes.Voucher,
            StepName = Workflows.AuthorizationFlow.Steps.SecurityLog,
            Step = Workflows.AuthorizationFlow.Ordinals.SecurityLog)]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> GetSecurityLogAsync(
                StorageResources<VoucherToken> voucherQuery,
                EastFive.Api.Security security,

            MultipartAsyncResponse<SecurityLog> onComplete)
        {
            var results = await voucherQuery
                .StorageGet()
                .Select(q => new SecurityLog(q))
                .ToArrayAsync();
            var sorted = new List<SecurityLog>(results);
            sorted.Sort();
            return onComplete(sorted.AsAsync());
        }

        [HttpAction("ActivityLog")]
        [EastFive.Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Version = Workflows.AuthorizationFlow.Version,
            Scope = Workflows.AuthorizationFlow.Scopes.Voucher,
            StepName = Workflows.AuthorizationFlow.Steps.ActivityLog,
            Step = Workflows.AuthorizationFlow.Ordinals.ActivityLog)]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> GetActivityLogAsync(
                [WorkflowParameter(
                    Value = Workflows.AuthorizationFlow.Variables.VoucherId.Set.Value,
                    Description = "Blank or " + Workflows.AuthorizationFlow.Variables.VoucherId.Set.Description)]
                [OptionalQueryParameter(Name = IdPropertyName)] IRefOptional<VoucherToken> voucherRefMaybe,

                [WorkflowParameter(
                    Value = Workflows.AuthorizationFlow.Variables.MonitoringRoute.Set.Value,
                    Description = Workflows.AuthorizationFlow.Variables.MonitoringRoute.Set.Description)]
                [OptionalQueryParameter] string route,

                [WorkflowParameter(
                    Value = Workflows.AuthorizationFlow.Variables.MonitoringMethod.Set.Value,
                    Description = Workflows.AuthorizationFlow.Variables.MonitoringMethod.Set.Description)]
                [OptionalQueryParameter] string method,

                [WorkflowParameter(
                    Value = Workflows.AuthorizationFlow.Variables.MonitoringWhen.Set.Value,
                    Description = Workflows.AuthorizationFlow.Variables.MonitoringWhen.Set.Description)]
                [QueryParameter] DateTime when,

                EastFive.Api.Security security,

            MultipartAsyncResponse<Api.Azure.Monitoring.MonitoringRequest.ActivityLog> onComplete,
            BadRequestResponse onBadRequest)
        {
            var actorIdMaybe = await voucherRefMaybe.StorageGetAsync(
                (x) => x.authId,
                () => default(Guid?));
            if (voucherRefMaybe.HasValueNotNull() && actorIdMaybe.IsDefault())
                return onBadRequest().AddReason($"The voucher ID `{voucherRefMaybe.id}` does not have an authorization id");

            if (actorIdMaybe.IsDefault() && string.IsNullOrWhiteSpace(route) && string.IsNullOrWhiteSpace(method))
                return onBadRequest().AddReason("You must have one of id, route, or method");

            var results = await Api.Azure.Monitoring.MonitoringRequest.GetActivityLog(actorIdMaybe, route, method, when)
                .ToArrayAsync();
            var sorted = new List<Api.Azure.Monitoring.MonitoringRequest.ActivityLog>(results);
            sorted.Sort();
            return onComplete(sorted.AsAsync());
        }

        public struct VoucherReport : IComparable<VoucherReport>
        {
            public string description;
            public DateTime expiration;
            public Guid id;

            public VoucherReport(VoucherToken token)
            {
                description = token.description;
                expiration = token.expiration;
                id = token.id;
            }

            public int CompareTo(VoucherReport other)
            {
                // sort by expiration, then description
                if (expiration == other.expiration)
                    return description.CompareTo(other.description);

                // ascending
                return expiration.CompareTo(other.expiration);
            }
        }

        [HttpAction("ListVouchers")]
        [EastFive.Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Version = Workflows.AuthorizationFlow.Version,
            Scope = Workflows.AuthorizationFlow.Scopes.Voucher,
            StepName = Workflows.AuthorizationFlow.Steps.ListVouchers,
            Step = Workflows.AuthorizationFlow.Ordinals.ListVouchers)]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> GetReportAsync(
                [WorkflowParameter(
                    Value = Workflows.AuthorizationFlow.Variables.ShowExpired.Set.Value,
                    Description = Workflows.AuthorizationFlow.Variables.ShowExpired.Set.Description)]
                [OptionalQueryParameter(Name = "show_expired")] bool showExpired,

                StorageResources<VoucherToken> voucherQuery,

            MultipartAsyncResponse<VoucherReport> onComplete)
        {
            var endDate = showExpired ? AzureStorageHelpers.MinDate : DateTime.UtcNow.Date;
            var results = await voucherQuery
                .Where(q => q.expiration >= endDate)
                .StorageGet()
                .Select(q => new VoucherReport(q))
                .ToArrayAsync();
            var sorted = new List<VoucherReport>(results);
            sorted.Sort();
            return onComplete(sorted.AsAsync());
        }

        [HttpAction("ChooseVoucher")]
        [EastFive.Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Version = Workflows.AuthorizationFlow.Version,
            Scope = Workflows.AuthorizationFlow.Scopes.Voucher,
            StepName = Workflows.AuthorizationFlow.Steps.ChooseVoucher,
            Step = Workflows.AuthorizationFlow.Ordinals.ChooseVoucher)]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> ChooseAsync(
                [WorkflowParameter(
                    Value = Workflows.AuthorizationFlow.Variables.VoucherId.Set.Value,
                    Description = Workflows.AuthorizationFlow.Variables.VoucherId.Set.Description)]
                [OptionalQueryParameter(Name = IdPropertyName)] Guid? idMaybe,

                [WorkflowParameter(
                    Value = Workflows.AuthorizationFlow.Variables.AuthId.Set.Value,
                    Description = Workflows.AuthorizationFlow.Variables.AuthId.Set.Description)]
                [OptionalQueryParameter(Name = AuthIdPropertyName)] Guid? authIdMaybe,

                [WorkflowParameter(
                    Value = "",
                    Description = "Voucher Key")]
                [OptionalQueryParameter(Name = KeyPropertyName)] string keyMaybe,

            [WorkflowVariable(
                Workflows.AuthorizationFlow.Variables.VoucherId.Get.Value,
                IdPropertyName)]
            ContentTypeResponse<VoucherToken> onFound,
            NotFoundResponse onNotFound,
            BadRequestResponse onBadRequest)
        {
            if (idMaybe.HasValue)
                return await idMaybe.Value.AsRef<VoucherToken>().StorageGetAsync(
                    (x) => onFound(x.PopulateToken(keyMaybe)),
                    () => onNotFound());

            if (authIdMaybe.HasValue)
                return await FindByAuthId(authIdMaybe.Value)
                    .FirstAsync(
                        (x) => onFound(x),
                        () => onNotFound());

            return onBadRequest().AddReason($"Either {IdPropertyName} or {AuthIdPropertyName} must be provided.");
        }

        private VoucherToken PopulateToken(string key)
        {
            return VoucherTools.GenerateUrlToken(this.id, this.expiration, key,
                token =>
                {
                    this.token = token;
                    this.key = key;
                    return this;
                },
                (why) => this);
        }

        [EastFive.Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Version = Workflows.AuthorizationFlow.Version,
            Scope = Workflows.AuthorizationFlow.Scopes.Voucher,
            StepName = Workflows.AuthorizationFlow.Steps.CreateVoucher,
            Step = Workflows.AuthorizationFlow.Ordinals.CreateVoucher)]
        [Api.HttpPost]
        public async static Task<IHttpResponse> CreateAsync(
                [Api.Meta.Flows.WorkflowNewId]
                [Property(Name = IdPropertyName)]IRef<VoucherToken> voucherTokenRef,
                
                [Api.Meta.Flows.WorkflowParameter(Value = "{{XAuthorization}}", Description = "performingAsActorId")]
                [PropertyOptional(Name = AuthIdPropertyName)]Guid authorizationId,

                [Api.Meta.Flows.WorkflowParameter(Value = "PFJTQ...1ZT4=", Description = "Found in Appsettings as EastFive.Security.CredentialProvider.Voucher.Key")]
                [Property(Name = KeyPropertyName)] string key,

                [Api.Meta.Flows.WorkflowParameter(Value = "{{$randomDateFuture}}")]
                [Property(Name = ExpirationPropertyName)] DateTime expiration,

                [Api.Meta.Flows.WorkflowParameter(Value = "", Description = "Who will use this token?")]
                [Property(Name = DescriptionPropertyName)] string description,

                [Api.Meta.Flows.WorkflowParameter(Value = "", Description = "Who created this token?")]
                [Property(Name = LastModifiedByPropertyName)] string lastModifiedBy,

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
            if (string.IsNullOrWhiteSpace(description))
                return onFailure($"Please include a {DescriptionPropertyName}.");

            if (string.IsNullOrWhiteSpace(lastModifiedBy))
                return onFailure($"Please include a {LastModifiedByPropertyName} person.");

            if (authorizationId.IsDefault())
                return onFailure($"Please include a {AuthIdPropertyName}.");

            return await VoucherTools.GenerateUrlToken(voucherTokenRef.id, expiration,
                    key,
                token =>
                {
                    voucherToken.keySignature = key.MD5HashGuid().ToString("N");
                    voucherToken.token = token;
                    voucherToken.key = default; // Don't save or return the key

                    voucherToken.claims = voucherToken.claims
                        .NullToEmpty()
                        .Append(Api.AppSettings.ActorIdClaimType.ConfigurationString(
                            (accountIdClaimType) => accountIdClaimType.PairWithValue(authorizationId.ToString("N"))))
                        .Concat(extraClaims.NullToEmpty())
                        .Distinct(kvp => kvp.Key)
                        .ToDictionary();

                    return voucherToken.StorageCreateAsync(
                        createdId =>
                        {
                            return onCreated(voucherToken);
                        },
                        () => onAlreadyExists());
                },
                (why) => onFailure(why).AsTask());
        }

        [HttpPatch]
        [EastFive.Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Version = Workflows.AuthorizationFlow.Version,
            Scope = Workflows.AuthorizationFlow.Scopes.Voucher,
            StepName = Workflows.AuthorizationFlow.Steps.UpdateVoucher,
            Step = Workflows.AuthorizationFlow.Ordinals.UpdateVoucher)]
        [SuperAdminClaim()]
        public static async Task<IHttpResponse> UpdateAsync(
                [WorkflowParameterFromVariable(
                    Value = Workflows.AuthorizationFlow.Variables.VoucherId.Get.Value,
                    Description = Workflows.AuthorizationFlow.Variables.VoucherId.Get.Description)]
                [UpdateId(Name = IdPropertyName)] IRef<VoucherToken> voucherRef,

                [WorkflowParameter(
                    Value = Workflows.AuthorizationFlow.Variables.VoucherDescription.Set.Value,
                    Description = Workflows.AuthorizationFlow.Variables.VoucherDescription.Set.Description)]
                [PropertyOptional(Name = DescriptionPropertyName)]Property<string> descriptionMaybe,

                [WorkflowParameter(
                    Value = Workflows.AuthorizationFlow.Variables.VoucherExpiration.Set.Value,
                    Description = Workflows.AuthorizationFlow.Variables.VoucherExpiration.Set.Description)]
                [Property(Name = ExpirationPropertyName)] DateTime? expirationMaybe,

                [Property(Name = KeyPropertyName)] Property<string> keyMaybe,

            ContentTypeResponse<VoucherToken> onFound,
            NotFoundResponse onNotFound,
            BadRequestResponse onBadRequest)
        {
            if (expirationMaybe.HasValue && expirationMaybe.Value < AzureStorageHelpers.MinDate)
                return onBadRequest().AddReason($"The minimum date is {AzureStorageHelpers.MinDate}");

            return await voucherRef.StorageCreateOrUpdateAsync(
                async (created, item, saveAsync) =>
                {
                    if (created)
                        return onNotFound();

                    bool modified() => (descriptionMaybe.specified && item.description != descriptionMaybe.value) ||
                                       (expirationMaybe.HasValue && item.expiration != expirationMaybe.Value);
                    if (modified())
                    {
                        if (descriptionMaybe.specified)
                            item.description = descriptionMaybe.value;

                        if (expirationMaybe.HasValue)
                            item.expiration = expirationMaybe.Value;

                        if (string.IsNullOrWhiteSpace(item.description))
                            return onBadRequest().AddReason($"The user issuing this request has a blank description.");

                        item.lastModifiedBy = $"{item.description} (id={item.id})";

                        await saveAsync(item);

                        if (!keyMaybe.specified)
                            return onFound(item);

                        return VoucherTools.GenerateUrlToken(voucherRef.id, item.expiration,
                                keyMaybe.value,
                            token =>
                            {
                                item.token = token;
                                return onFound(item);
                            },
                            (why) => onBadRequest().AddReason(why));

                    }
                    return onFound(item);
                });
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

        public static IEnumerableAsync<VoucherToken> FindByAuthId(Guid authId)
        {
            var query = new TableQuery<GenericTableEntity>().Where(
                TableQuery.GenerateFilterConditionForGuid(AuthIdPropertyName, QueryComparisons.Equal, authId));
            return query.FilterString
                .StorageFindbyQuery<VoucherToken>();
        }
    }
}
