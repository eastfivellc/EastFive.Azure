using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Azure.Auth;
using EastFive.Persistence.Azure.StorageTables;

using Newtonsoft.Json;
using EastFive.Api.Azure;
using EastFive.Extensions;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Linq.Async;
using EastFive.Api.Serialization;
using System.Reflection;
using EastFive.Persistence.Azure.StorageTables.Driver;
using Microsoft.WindowsAzure.Storage.Table;
using EastFive.Persistence;
using EastFive.Linq.Expressions;
using EastFive.Linq;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Collections.Generic;

namespace EastFive.Azure.Persistence
{
    [FunctionViewController6(
        Route = "StorageTable",
        Resource = typeof(StorageTable),
        ContentType = "x-application/eastfive.azure.storage.table",
        ContentTypeVersion = "0.1")]
    [DataContract]
    [StorageTable]
    public class StorageTable : IReferenceable
    {
        #region Properties

        [JsonIgnore]
        public Guid id => this.storageTableRef.id;

        public const string IdPropertyName = "id";
        [JsonProperty(PropertyName = IdPropertyName)]
        [ApiProperty(PropertyName = IdPropertyName)]
        public IRef<StorageTable> storageTableRef;

        public const string NamePropertyName = "name";
        [JsonProperty(PropertyName = NamePropertyName)]
        public string name;

        [JsonIgnore]
        public Type type;

        [JsonProperty]
        public StorageProperty[] properties;

        [JsonIgnore]
        private CloudTable cloudTable;

        #endregion

        #region Http Methods

        [Api.HttpGet]
        [RequiredClaim(
            Microsoft.IdentityModel.Claims.ClaimTypes.Role,
            ClaimValues.Roles.SuperAdmin)]
        public static async Task<HttpResponseMessage> All(
            HttpApplication httpApp,
            ContentTypeResponse<StorageTable[]> onFound)
        {
            var limitedAssemblyQuery = httpApp.GetType()
                .GetAttributesInterface<IApiResources>(inherit: true, multiple: true);
            Func<Assembly, bool> shouldCheckAssembly =
                (assembly) =>
                {
                    return limitedAssemblyQuery
                        .First(
                            (limitedAssembly, next) =>
                            {
                                if (limitedAssembly.ShouldCheckAssembly(assembly))
                                    return true;
                                return next();
                            },
                            () => false);
                };
            var tables = DiscoverStorageResources(shouldCheckAssembly).ToArray();
            return onFound(tables);
        }

        [Api.HttpGet]
        [RequiredClaim(
            Microsoft.IdentityModel.Claims.ClaimTypes.Role,
            ClaimValues.Roles.SuperAdmin)]
        public static async Task<HttpResponseMessage> List(
                [QueryParameter(Name = NamePropertyName)]string name,
                HttpApplication httpApp,
                HttpRequestMessage request,
            NotFoundResponse onNotFound)
        {
            var limitedAssemblyQuery = httpApp.GetType()
                .GetAttributesInterface<IApiResources>(inherit: true, multiple: true);
            Func<Assembly, bool> shouldCheckAssembly =
                (assembly) =>
                {
                    return limitedAssemblyQuery
                        .First(
                            (limitedAssembly, next) =>
                            {
                                if (limitedAssembly.ShouldCheckAssembly(assembly))
                                    return true;
                                return next();
                            },
                            () => false);
                };
            return await DiscoverStorageResources(shouldCheckAssembly)
                .Where(table => table.name == name)
                .First(
                    async (storageTable, next) =>
                    {
                        var table = storageTable.cloudTable;
                        var query = new TableQuery<GenericTableEntity>();
                        var allRows = await AzureTableDriverDynamic
                            .FindAllInternal(query, table)
                            .Select(
                                tableRow =>
                                {
                                    return new StorageRow()
                                    {
                                        rowKey = tableRow.RowKey,
                                        partitionKey = tableRow.PartitionKey,
                                        properties = tableRow.properties
                                            .Select(
                                                property =>
                                                {
                                                    var epValue = property.Value;
                                                    object GetValue()
                                                    {
                                                        if (epValue.PropertyType == EdmType.String)
                                                            return epValue.StringValue;
                                                        if (epValue.PropertyType == EdmType.DateTime)
                                                            return epValue.DateTime;
                                                        if (epValue.PropertyType == EdmType.Binary)
                                                            return epValue.BinaryValue;
                                                        if (epValue.PropertyType == EdmType.Boolean)
                                                            return epValue.BooleanValue;
                                                        if (epValue.PropertyType == EdmType.Double)
                                                            return epValue.DoubleValue;
                                                        if (epValue.PropertyType == EdmType.Guid)
                                                            return epValue.GuidValue;
                                                        if (epValue.PropertyType == EdmType.Int32)
                                                            return epValue.Int32Value;
                                                        if (epValue.PropertyType == EdmType.Int64)
                                                            return epValue.Int64Value;
                                                        return null;
                                                    }
                                                    return property.Key
                                                        .PairWithValue(GetValue());
                                                })
                                            .ToDictionary(),
                                    };
                                })
                            .ToArrayAsync();

                        var response = request.CreateResponse(System.Net.HttpStatusCode.OK);
                        var converter = new ExtrudeConvert(httpApp as HttpApplication, request);
                        var jsonObj = JsonConvert.SerializeObject(allRows,
                            new JsonSerializerSettings
                            {
                                Converters = new JsonConverter[] { converter }.ToList(),
                            });
                        response.Content = new StringContent(jsonObj, Encoding.UTF8, "application/json");
                        return response;
                    },
                    () => onNotFound().AsTask());
        }

        [Api.HttpGet]
        [RequiredClaim(
            Microsoft.IdentityModel.Claims.ClaimTypes.Role, 
            ClaimValues.Roles.SuperAdmin)]
        public static async Task<HttpResponseMessage> List2(
                [QueryParameter(Name = NamePropertyName)]string name,
                HttpApplication httpApp,
                HttpRequestMessage request,
            NotFoundResponse onNotFound)
        {
            var limitedAssemblyQuery = httpApp.GetType()
                .GetAttributesInterface<IApiResources>(inherit: true, multiple: true);
            Func<Assembly, bool> shouldCheckAssembly =
                (assembly) =>
                {
                    return limitedAssemblyQuery
                        .First(
                            (limitedAssembly, next) =>
                            {
                                if (limitedAssembly.ShouldCheckAssembly(assembly))
                                    return true;
                                return next();
                            },
                            () => false);
                };
            return await DiscoverStorageResources(shouldCheckAssembly)
                .Where(table => table.name == name)
                .First(
                    async (storageTable, next) =>
                    {
                        var tableData = await storageTable.type.StorageGetAll().ToArrayAsync();

                        var response = request.CreateResponse(System.Net.HttpStatusCode.OK);
                        var converter = new ExtrudeConvert(httpApp as HttpApplication, request);
                        var jsonObj = Newtonsoft.Json.JsonConvert.SerializeObject(tableData,
                            new JsonSerializerSettings
                            {
                                Converters = new JsonConverter[] { converter }.ToList(),
                            });
                        response.Content = new StringContent(jsonObj, Encoding.UTF8, "application/json");
                        return response;
                    },
                    () => onNotFound().AsTask());
        }

        #endregion

        public static IEnumerable<StorageTable> DiscoverStorageResources(Func<Assembly, bool> shouldCheckAssembly)
        {
            var driver = AzureTableDriverDynamic.FromSettings();
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(shouldCheckAssembly)
                .SelectMany(
                    a =>
                    {
                        try
                        {
                            return a.GetTypes();
                        } catch (Exception)
                        {
                            return new Type[] { };
                        }
                    })
                .SelectMany(
                    (type) =>
                    {
                        return type
                            .GetAttributesInterface<IProvideTable>()
                            .Select(
                                tableProvider =>
                                {
                                    var table = tableProvider.GetTable(type, driver.TableClient);
                                    var properties = type
                                        .StorageProperties()
                                        .Select(
                                            propInfoAttribute =>
                                            {
                                                var propInfo = propInfoAttribute.Key;
                                                var name = propInfo.GetTablePropertyName();
                                                var memberType = propInfo.GetMemberType();
                                                return new StorageProperty()
                                                {
                                                    name = name,
                                                    type = memberType,
                                                };
                                            })
                                        .ToArray();
                                    return new StorageTable
                                    {
                                        name = table.Name,
                                        properties = properties,
                                        type = type,
                                        cloudTable = table,
                                    };
                                });
                    });
        }
    }
}
