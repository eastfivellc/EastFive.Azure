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

        public struct ActivityLog : IComparable<ActivityLog>
        {
            public string method;
            public string route;
            public int status;
            public DateTime timestamp;
            public string url;
            public Guid id;
            public KeyValuePair<string, string>[] formdata;
            public string blob;

            public ActivityLog(Api.Azure.Monitoring.MonitoringRequest request)
            {
                method = request.method;
                route = request.route;
                status = request.status;
                timestamp = request.when;
                url = request.url.AbsoluteUri;
                id = request.id;
                formdata = request.formData
                    .NullToEmpty()
                    .Select(x => x.key.PairWithValue(x.contents.NullToEmpty().Join(",")))
                    .ToArray();
                blob = default(string);
            }

            public static async Task<ActivityLog> ConvertAsync(Api.Azure.Monitoring.MonitoringRequest request)
            {
                var log = new ActivityLog(request);
                if (request.body != null)
                {
                    try
                    {
                        log.blob = await request.body.LoadStreamAsync(
                        (id, stream, mediaType, disposition) =>
                        {
                            using (var reader = new StreamReader(stream))
                            {
                                var json = reader.ReadToEnd();
                                return json;
                            }
                        },
                        () => default(string));
                    }
                    catch (Exception) { }
                }
                return log;
            }

            public int CompareTo(ActivityLog other)
            {
                // ascending
                return timestamp.CompareTo(other.timestamp);
            }
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
                    Description = Workflows.AuthorizationFlow.Variables.VoucherId.Set.Description)]
                [QueryParameter(Name = IdPropertyName)] IRef<VoucherToken> voucherRef,

                [WorkflowParameter(
                    Value = Workflows.AuthorizationFlow.Variables.MonitoringStart.Set.Value,
                    Description = Workflows.AuthorizationFlow.Variables.MonitoringStart.Set.Description)]
                [QueryParameter(Name = "start")] DateTime start,

                EastFive.Api.Security security,

            MultipartAsyncResponse<ActivityLog> onComplete,
            BadRequestResponse onBadRequest)
        {
            var voucherActorId = await voucherRef.StorageGetAsync(
                (x) => x.authId,
                () => default(Guid?));
            if (voucherActorId.IsDefault())
                return onBadRequest().AddReason($"The voucher ID `{voucherRef.id}` does not have an authorization id");

            var r = Guid.Parse("0036f91c8d22489495df94e36b5774cd").AsRef<Api.Azure.Monitoring.MonitoringRequest>();
            var results = await start
                .StorageGetBy((Api.Azure.Monitoring.MonitoringRequest mr) => mr.when)
                .Where(mr =>
                {
                    var tryAuth = mr.headers
                        .NullToEmpty()
                        .First(
                            (x, next) =>
                            {
                                if (x.key == "Authorization")
                                    return x.value;

                                return next();
                            },
                            () => default(string));
                    if (string.IsNullOrWhiteSpace(tryAuth))
                        return false;

                    var actorId = tryAuth.GetClaimsJwtString(
                            (claims) => claims.GetActorId(x => x, () => default(Guid?)),
                            (why) => default(Guid?));

                    return actorId == voucherActorId;
                })
                .Select(ActivityLog.ConvertAsync)
                .Await(readAhead: 25)
                .ToArrayAsync();

            //var v = await r.StorageGetAsync(
            //    (IQueryable<Api.Azure.Monitoring.MonitoringRequest> mr) => mr
            //        .Where(m => m.when == start),
            //    (mr) => mr,
            //    () => default(Api.Azure.Monitoring.MonitoringRequest));

            //var results = await voucherQuery
            //    .StorageGet()
            //    .Select(q => new ActivityLog(q))
            //    .ToArrayAsync();
            var sorted = new List<ActivityLog>(results);
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
                [QueryParameter(Name = "show_expired")] bool showExpired,

                StorageResources<VoucherToken> voucherQuery,
                EastFive.Api.Security security,

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
                [QueryParameter(Name = IdPropertyName)] Guid? idMaybe,

                [WorkflowParameter(
                    Value = Workflows.AuthorizationFlow.Variables.AuthId.Set.Value,
                    Description = Workflows.AuthorizationFlow.Variables.AuthId.Set.Description)]
                [QueryParameter(Name = AuthIdPropertyName)] Guid? authIdMaybe,

                EastFive.Api.Security security,

            [WorkflowVariable(
                Workflows.AuthorizationFlow.Variables.VoucherId.Get.Value,
                IdPropertyName)]
            ContentTypeResponse<VoucherToken> onFound,
            NotFoundResponse onNotFound,
            BadRequestResponse onBadRequest)
        {
            if (idMaybe.HasValue)
                return await idMaybe.Value.AsRef<VoucherToken>().StorageGetAsync(
                    (x) => onFound(x),
                    () => onNotFound());

            if (authIdMaybe.HasValue)
                return await FindByAuthId(authIdMaybe.Value)
                    .FirstAsync(
                        (x) => onFound(x),
                        () => onNotFound());

            return onBadRequest().AddReason($"Either {IdPropertyName} or {AuthIdPropertyName} must be provided.");
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

                [Api.Meta.Flows.WorkflowParameter(Value = "PFJTQ...1ZT4=", Description = "Found in Appsettings as EastFive.Security.Token.Key")]
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
                        .Distinct(kvp => kvp.Key)
                        .ToDictionary();

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

        [HttpPatch]
        [EastFive.Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Version = Workflows.AuthorizationFlow.Version,
            Scope = Workflows.AuthorizationFlow.Scopes.Voucher,
            StepName = Workflows.AuthorizationFlow.Steps.UpdateVoucher,
            Step = Workflows.AuthorizationFlow.Ordinals.UpdateVoucher)]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> UpdateAsync(
                [WorkflowParameterFromVariable(
                    Value = Workflows.AuthorizationFlow.Variables.VoucherId.Get.Value,
                    Description = Workflows.AuthorizationFlow.Variables.VoucherId.Get.Description)]
                [Property(Name = IdPropertyName)] IRef<VoucherToken> voucherRef,

                [WorkflowParameter(
                    Value = Workflows.AuthorizationFlow.Variables.VoucherDescription.Set.Value,
                    Description = Workflows.AuthorizationFlow.Variables.VoucherDescription.Set.Description)]
                [Property(Name = DescriptionPropertyName)] string description,

                [WorkflowParameter(
                    Value = Workflows.AuthorizationFlow.Variables.VoucherExpiration.Set.Value,
                    Description = Workflows.AuthorizationFlow.Variables.VoucherExpiration.Set.Description)]
                [Property(Name = ExpirationPropertyName)] DateTime? expirationMaybe,

                EastFive.Api.Security security,

            ContentTypeResponse<VoucherToken> onFound,
            NotFoundResponse onNotFound,
            BadRequestResponse onBadRequest)
        {
            if (expirationMaybe.HasValue && expirationMaybe.Value < AzureStorageHelpers.MinDate)
                return onBadRequest().AddReason($"The minimum date is {AzureStorageHelpers.MinDate}");

            var lastModifiedByTask = FindByAuthId(security.performingAsActorId)
                .FirstAsync(
                    (item) => item,
                    () => default(VoucherToken?));
            return await voucherRef.StorageCreateOrUpdateAsync(
                async (created, item, saveAsync) =>
                {
                    if (created)
                        return onNotFound();

                    bool modified() => (description.HasBlackSpace() && item.description != description) ||
                                       (expirationMaybe.HasValue && item.expiration != expirationMaybe.Value);
                    if (modified())
                    {
                        if (description.HasBlackSpace())
                            item.description = description;

                        if (expirationMaybe.HasValue)
                            item.expiration = expirationMaybe.Value;

                        var lastModifiedBy = await lastModifiedByTask;
                        if (string.IsNullOrWhiteSpace(lastModifiedBy.description))
                            return onBadRequest().AddReason($"The user issuing this request has a blank description.");

                        item.lastModifiedBy = $"{lastModifiedBy.description} (id={lastModifiedBy.id})";
                        await saveAsync(item);
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
