﻿using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Security.Claims;

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
using EastFive.Persistence;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using EastFive.Linq;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Collections.Generic;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Persistence
{
    [FunctionViewController(
        Route = "StorageTable",
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
        [SuperAdminClaim]
        public static IHttpResponse All(
            HttpApplication httpApp,
            ContentTypeResponse<StorageTable[]> onFound)
        {
            var tables = DiscoverStorageResources(httpApp.GetType()).ToArray();
            return onFound(tables);
        }

        [Api.HttpGet]
        [SuperAdminClaim]
        public static IHttpResponse List(
                [QueryParameter(Name = NamePropertyName)]string name,
                HttpApplication httpApp,
            MultipartAsyncResponse<StorageRow> onFound,
            NotFoundResponse onNotFound)
        {
            return DiscoverStorageResources(httpApp.GetType())
                .Where(table => table.name == name)
                .First(
                    (storageTable, next) =>
                    {
                        var table = storageTable.cloudTable;
                        var query = new TableQuery<GenericTableEntity>();
                        var allRows = AzureTableDriverDynamic
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
                                                    var epValue = property.Value
                                                        .GetPropertyAsObject(out bool hasValue);
                                                    
                                                    return property.Key
                                                        .PairWithValue(epValue);
                                                })
                                            .ToDictionary(),
                                    };
                                });
                        return onFound(allRows);
                    },
                    () => onNotFound());
        }

        [Api.HttpAction("GenerateCryptoKey")]
        [SuperAdminClaim]
        public static IHttpResponse GenerateCryptoKey(
            TextResponse onGenerated)
        {
            var bytes = Security.SecureGuid.Generate().ToByteArray()
                .Concat(Security.SecureGuid.Generate().ToByteArray())
                .ToArray();
            var byteKey = bytes.ToBase64String();
            return onGenerated(byteKey);
        }

        [Api.HttpAction("Information")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> Information(
                [QueryParameter(Name = NamePropertyName)]string name,
                HttpApplication httpApp,
            ContentTypeResponse<TableInformation> onFound,
            NotFoundResponse onNotFound)
        {
            return await DiscoverStorageResources(httpApp.GetType())
                .Where(table => table.name == name)
                .First(
                    async (storageTable, next) =>
                    {
                        var information = await storageTable.type
                            .StorageTableInformationAsync(storageTable.cloudTable);
                        return onFound(information);
                    },
                    () => onNotFound().AsTask());
        }

        [Api.HttpAction("PropertyInformation")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> PropertyInformation(
                [QueryParameter(Name = "table")]string tableName,
                [QueryParameter(Name = "property")]string propertyName,
                HttpApplication httpApp,
            ContentTypeResponse<PropertyLookupInformation[]> onFound,
            NotFoundResponse onNotFound,
            BadRequestResponse onPropertyDoesNotSupportFindBy)
        {
            return await DiscoverStorageResources(httpApp.GetType())
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
                                () =>
                                {
                                    return onNotFound().AsTask();
                                });
                    },
                    () => onNotFound().AsTask());
        }

        [Api.HttpGet]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> List2(
                [QueryParameter(Name = NamePropertyName)]string name,
                HttpApplication httpApp,
                IHttpRequest request,
            NotFoundResponse onNotFound)
        {
            return await DiscoverStorageResources(httpApp.GetType())
                .Where(table => table.name == name)
                .First(
                    async (storageTable, next) =>
                    {
                        var tableData = await storageTable.type.StorageGetAll().ToArrayAsync();

                        var response = request.CreateResponse(System.Net.HttpStatusCode.OK, tableData);
                        return response;
                    },
                    () => onNotFound().AsTask());
        }

        #endregion

        public static IEnumerable<StorageTable> DiscoverStorageResources(Type appType)
        {
            var limitedAssemblyQuery = appType
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
                                                    member = propInfo,
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
