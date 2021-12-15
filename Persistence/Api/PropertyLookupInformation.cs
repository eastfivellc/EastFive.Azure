using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Security.Claims;

using Newtonsoft.Json;

using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Azure.Auth;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Api.Serialization;
using System.Reflection;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Persistence;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using EastFive.Linq;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Collections.Generic;

namespace EastFive.Azure.Persistence
{
    [FunctionViewController(
        Route = "PropertyLookupInformation",
        ContentType = "x-application/eastfive.azure.storage.property-lookup-info",
        ContentTypeVersion = "0.1")]
    public class PropertyLookupInformation
    {
        #region Properties

        public const string RowKeyPropertyName = "row_key";
        [JsonProperty(PropertyName = RowKeyPropertyName)]
        public string rowKey;

        public const string PartitionKeyPropertyName = "partition_key";
        [JsonProperty(PropertyName = PartitionKeyPropertyName)]
        public string partitionKey;

        public const string ValuePropertyName = "value";
        [JsonProperty(PropertyName = ValuePropertyName)]
        public object value;

        public const string CountPropertyName = "count";
        [JsonProperty(PropertyName = CountPropertyName)]
        public long count;

        public const string KeysPropertyName = "keys";
        [JsonProperty(PropertyName = KeysPropertyName)]
        public string [] keys;

        #endregion

        #region Http Methods

        [Api.HttpGet]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> PropertyInformation(
                [QueryParameter(Name = "table")]string tableName,
                [QueryParameter(Name = "property")]string propertyName,
                IApplication httpApp,
            ContentTypeResponse<PropertyLookupInformation[]> onFound,
            NotFoundResponse onNotFound,
            BadRequestResponse onPropertyDoesNotSupportFindBy)
        {
            return await StorageTable.DiscoverStorageResources(httpApp.GetType())
                .Where(table => table.name == tableName)
                .First(
                    (storageTable, next) =>
                    {
                        return storageTable.properties
                            .Where(prop => prop.name == propertyName)
                            .First(
                                async (prop, next2) =>
                                {
                                    if (!prop.member.ContainsAttributeInterface<IProvideFindBy>())
                                        return onPropertyDoesNotSupportFindBy();
                                    var findBy = prop.member.GetAttributeInterface<IProvideFindBy>();
                                    var information = await findBy.GetInfoAsync(prop.member);
                                    return onFound(information);
                                },
                                () => onNotFound().AsTask());
                    },
                    () => onNotFound().AsTask());
        }

        #endregion

    }
}
