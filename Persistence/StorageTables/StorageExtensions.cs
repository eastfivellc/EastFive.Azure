using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

using EastFive.Async;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using EastFive.Persistence.Azure.StorageTables.Driver;
using BlackBarLabs.Persistence.Azure.StorageTables;
using BlackBarLabs.Persistence.Azure;
using EastFive.Persistence.Azure;
using EastFive.Azure.StorageTables.Driver;
using System.Reflection;
using System.IO;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Persistence;
using EastFive.Analytics;
using System.Threading;
using EastFive.Azure.Persistence.StorageTables;
using Microsoft.Azure.Cosmos.Table;
using EastFive.Api;

namespace EastFive.Azure.Persistence.AzureStorageTables
{
    public static class StorageExtensions
    {
        #region Row / Partition keys

        public static string StorageComputeRowKey<TEntity>(this IRef<TEntity> entityRef,
                Func<EastFive.Persistence.IComputeAzureStorageTableRowKey> onMissing = default)
            where TEntity : IReferenceable
        {
            var rowKeyMember = typeof(TEntity)
                .GetPropertyOrFieldMembers()
                .Where(member => member.ContainsAttributeInterface<EastFive.Persistence.IComputeAzureStorageTableRowKey>())
                .Select(member =>
                    member.GetAttributesInterface<EastFive.Persistence.IComputeAzureStorageTableRowKey>()
                        .First()
                        .PairWithKey(member))
                .First(
                    (computeAzureStorageTableRowKey, next) => computeAzureStorageTableRowKey,
                    () =>
                    {
                        if (onMissing.IsDefaultOrNull())
                            throw new Exception($"{typeof(TEntity).FullName} is missing attribute implementing {typeof(EastFive.Persistence.IComputeAzureStorageTableRowKey).FullName}.");
                        return onMissing().PairWithKey(default(MemberInfo));
                    });
            return rowKeyMember.Value.ComputeRowKey(entityRef, rowKeyMember.Key);
        }

        public static string StorageComputeRowKey<TEntity>(this IQueryable<TEntity> entityQuery,
                Func<EastFive.Persistence.IComputeAzureStorageTableRowKey> onMissing = default)
            where TEntity : IReferenceable
        {
            var extraValues = entityQuery
                .Compile<IEnumerable<Reflection.Assignment>, IProvideQueryValues>(
                    Enumerable.Empty<Reflection.Assignment>(),
                    (extraValuesCurrent, attr, methodInfo, methodArguments) =>
                    {
                        var queryValue = attr.GetStorageValues(methodInfo, methodArguments);
                        return extraValuesCurrent.Concat(queryValue);
                    },
                    (extraValuesCurrent, unrecognizedMethod, methodArguments) =>
                    {
                        if (unrecognizedMethod.Name == "Where")
                        {
                            return unrecognizedMethod.TryParseMemberAssignment(methodArguments,
                                (memberInfo, expressionType, memberValue) =>
                                    extraValuesCurrent.Append(
                                        new Assignment
                                        {
                                            member = memberInfo,
                                            type = ExpressionType.Equal,
                                            value = memberValue,
                                        }),
                                () => throw new ArgumentException(
                                    $"Could not parse `{unrecognizedMethod}`({methodArguments})"));
                        }
                        // Don't throw here since query may include non-partition members
                        return extraValuesCurrent;
                    })
                .ToArray();

            return extraValues
                .Where(extraValue => extraValue.member
                    .ContainsAttributeInterface<EastFive.Persistence.IComputeAzureStorageTableRowKey>())
                .First<Assignment, string>(
                    (memberValueKvp, next) =>
                    {
                        var member = memberValueKvp.member;
                        var value = memberValueKvp.value;
                        var computeAzureStorageTableRowKey = member
                            .GetAttributeInterface<EastFive.Persistence.IComputeAzureStorageTableRowKey>();
                        var rowKey = computeAzureStorageTableRowKey.ComputeRowKey(value, member);
                        return rowKey;
                    },
                    () =>
                    {
                        var exMessage =
                            $"{typeof(TEntity).FullName} is missing attribute implementing" +
                            $" {typeof(EastFive.Persistence.IComputeAzureStorageTableRowKey).FullName}.";
                        throw new Exception(exMessage);
                    });
        }

        public static string StorageComputeRowKey(this MemberInfo memberInfo, object memberValue,
                Func<EastFive.Persistence.IComputeAzureStorageTableRowKey> onMissing = default)
        {
            return memberInfo
                .GetAttributesInterface<EastFive.Persistence.IComputeAzureStorageTableRowKey>()
                .First(
                    (computeAzureStorageTableRowKey, next) =>
                    {
                        return computeAzureStorageTableRowKey.ComputeRowKey(memberValue, memberInfo);
                    },
                    () =>
                    {
                        if (onMissing.IsDefaultOrNull())
                        {
                            throw new Exception(
                                $"{memberInfo.DeclaringType.FullName}..{memberInfo.Name} is missing attribute implementing" + 
                                $" {typeof(EastFive.Persistence.IComputeAzureStorageTableRowKey).FullName}.");
                        }
                        var computeAzureStorageTableRowKey = onMissing();
                        return computeAzureStorageTableRowKey.ComputeRowKey(memberValue, memberInfo);
                    });
        }

        public static string StorageGetRowKey<TEntity>(this TEntity entity)
        {
            entity.StorageTryGetRowKeyForType(typeof(TEntity), out string rowKey);
            return rowKey;
        }

        public static bool StorageHasRowKey(this Type entityType)
        {
            return entityType
                .GetPropertyOrFieldMembers()
                .Where(member => member.ContainsAttributeInterface<EastFive.Persistence.IModifyAzureStorageTableRowKey>())
                .Select(member =>
                    member.GetAttributesInterface<EastFive.Persistence.IModifyAzureStorageTableRowKey>()
                        .First()
                        .PairWithKey(member))
                .Any();
        }

        public static bool StorageTryGetRowKeyForType(this object entity, Type entityType, out string rowKey)
        {
            bool success = true;
            (success, rowKey) =  entityType
                .GetPropertyOrFieldMembers()
                .Where(member => member.ContainsAttributeInterface<EastFive.Persistence.IModifyAzureStorageTableRowKey>())
                .Select(member =>
                    member.GetAttributesInterface<EastFive.Persistence.IModifyAzureStorageTableRowKey>()
                        .First()
                        .PairWithKey(member))
                .First(
                    (partitionKeyMember, next) =>
                    {
                        var rk = partitionKeyMember.Value
                            .GenerateRowKey(entity, partitionKeyMember.Key);
                        return (true, rk);
                    },
                    () =>
                    {
                        return (false, "");
                    });
            return success;
        }

        public static TEntity StorageParseRowKey<TEntity>(this TEntity entity, string rowKey)
        {
            return (TEntity)entity.StorageParseRowKeyForType(rowKey, typeof(TEntity));
            //var partitionKeyMember = typeof(TEntity)
            //    .GetPropertyOrFieldMembers()
            //    .Where(member => member.ContainsAttributeInterface<EastFive.Persistence.IModifyAzureStorageTableRowKey>())
            //    .Select(member =>
            //        member.GetAttributesInterface<EastFive.Persistence.IModifyAzureStorageTableRowKey>()
            //            .First()
            //            .PairWithKey(member))
            //    .First();
            //return partitionKeyMember.Value.ParseRowKey(entity, rowKey, partitionKeyMember.Key);
        }

        public static object StorageParseRowKeyForType(this object entity, string rowKey, Type entityType)
        {
            var rowKeyMember = entityType
                .GetPropertyOrFieldMembers()
                .Where(member => member.ContainsAttributeInterface<EastFive.Persistence.IModifyAzureStorageTableRowKey>())
                .Select(member =>
                    member.GetAttributesInterface<EastFive.Persistence.IModifyAzureStorageTableRowKey>()
                        .First()
                        .PairWithKey(member))
                .First();
            return rowKeyMember.Value.ParseRowKey(entity, rowKey, rowKeyMember.Key);
        }

        public static string StorageComputePartitionKey<TEntity>(this IRef<TEntity> entityRef,
                Func<EastFive.Persistence.IComputeAzureStorageTablePartitionKey> onMissing = default)
            where TEntity : IReferenceable
        {
            var rowKey = entityRef.StorageComputeRowKey();
            return entityRef.StorageComputePartitionKey(rowKey, onMissing: onMissing);
        }

        public static string StorageComputePartitionKey<TEntity>(this IRef<TEntity> entityRef, string rowKey,
                Func<EastFive.Persistence.IComputeAzureStorageTablePartitionKey> onMissing = default)
            where TEntity : IReferenceable
        {
            var partitionKeyMember = typeof(TEntity)
                .GetPropertyOrFieldMembers()
                .Where(member => member.ContainsAttributeInterface<EastFive.Persistence.IComputeAzureStorageTablePartitionKey>())
                .Select(member =>
                    member.GetAttributesInterface<EastFive.Persistence.IComputeAzureStorageTablePartitionKey>()
                        .First()
                        .PairWithKey(member))
                .First(
                    (computeAzureStorageTableParitionKey, next) => computeAzureStorageTableParitionKey,
                    () =>
                    {
                        if (onMissing.IsDefaultOrNull())
                        {
                            var exMessage = $"{typeof(TEntity).FullName} is missing attribute implementing" +
                                " {typeof(EastFive.Persistence.IComputeAzureStorageTablePartitionKey).FullName}.";
                            throw new Exception(exMessage);
                        }
                        return onMissing().PairWithKey(default(MemberInfo));
                    });
            return partitionKeyMember.Value.ComputePartitionKey(entityRef, partitionKeyMember.Key, rowKey);
        }

        public static string StorageComputePartitionKey(this MemberInfo memberInfo, 
                object memberValue,
                string rowKey,
                Func<EastFive.Persistence.IComputeAzureStorageTablePartitionKey> onMissing = default)
        {
            return memberInfo
                .GetAttributesInterface<EastFive.Persistence.IComputeAzureStorageTablePartitionKey>()
                .First(
                    (computeAzureStorageTableParitionKey, next) =>
                    {
                        var partitionKey = computeAzureStorageTableParitionKey.ComputePartitionKey(memberValue, memberInfo, rowKey);
                        return partitionKey;
                    },
                    () =>
                    {
                        if (onMissing.IsDefaultOrNull())
                        {
                            var exMessage =
                                $"{memberInfo.DeclaringType.FullName}..{memberInfo.Name} is missing attribute implementing" +
                                $" {typeof(EastFive.Persistence.IComputeAzureStorageTablePartitionKey).FullName}.";
                            throw new Exception(exMessage);
                        }
                        return onMissing().ComputePartitionKey(memberValue, memberInfo, rowKey);
                    });
        }

        public static string StorageComputePartitionKey<TEntity>(this IQueryable<TEntity> query,
                string rowKey)
        {
            var extraValues = query
                .Compile<IEnumerable<Assignment>, IProvideQueryValues>(
                    Enumerable.Empty<Assignment>(),
                    (extraValuesCurrent, attr, methodInfo, methodArguments) =>
                    {
                        var queryValue = attr.GetStorageValues(methodInfo, methodArguments);
                        return extraValuesCurrent.Concat(queryValue);
                    },
                    (extraValuesCurrent, unrecognizedMethod, methodArguments) =>
                    {
                        if (unrecognizedMethod.Name == "Where")
                        {
                            return unrecognizedMethod.TryParseMemberAssignment(methodArguments,
                                (memberInfo, expressionType, memberValue) => 
                                    extraValuesCurrent.Append(
                                        new Assignment
                                        {
                                            member = memberInfo,
                                            type = ExpressionType.Equal,
                                            value = memberValue,
                                        }),
                                () => throw new ArgumentException(
                                    $"Could not parse `{unrecognizedMethod}`({methodArguments})"));
                        }
                        // Don't throw here since query may include non-partition members
                        return extraValuesCurrent;
                    })
                .ToArray();

            return extraValues
                .Where(extraValue => extraValue.member
                    .ContainsAttributeInterface<EastFive.Persistence.IComputeAzureStorageTablePartitionKey>())
                .First<Assignment, string>(
                    (memberValueKvp, next) =>
                    {
                        var member = memberValueKvp.member;
                        var value = memberValueKvp.value;
                        var computeAzureStorageTableParitionKey = member
                            .GetAttributeInterface<EastFive.Persistence.IComputeAzureStorageTablePartitionKey>();
                        var kvps = extraValues
                            .Select(extraValue => extraValue.member.PairWithValue(extraValue.value))
                            .ToArray();
                        var partitionKey = computeAzureStorageTableParitionKey.ComputePartitionKey(value, member, rowKey, kvps);
                        return partitionKey;
                    },
                    () =>
                    {
                        var exMessage =
                            $"{typeof(TEntity).FullName} is missing attribute implementing" +
                            $" {typeof(EastFive.Persistence.IComputeAzureStorageTablePartitionKey).FullName}.";
                        throw new Exception(exMessage);
                    });
        }

        public static bool StorageHasPartitionKey(this Type entityType)
        {
            return entityType
                .GetPropertyOrFieldMembers()
                .Where(member => member.ContainsAttributeInterface<IModifyAzureStorageTablePartitionKey>())
                .Any();
        }

        public static string StorageGetPartitionKey<TEntity>(this TEntity entity)
        {
            var rowKey = entity.StorageGetRowKey();
            entity.StorageTryGetPartitionKeyForType(rowKey, typeof(TEntity), 
                out string partitionKey);
            return partitionKey;
        }

        public static bool StorageTryGetPartitionKeyForType(this object entity, string rowKey, Type type,
            out string partitionKey)
        {
            bool success = true;
            (success, partitionKey) = type
                .GetPropertyOrFieldMembers()
                .Where(member => member.ContainsAttributeInterface<EastFive.Persistence.IModifyAzureStorageTablePartitionKey>())
                .Select(member =>
                    member.GetAttributesInterface<EastFive.Persistence.IModifyAzureStorageTablePartitionKey>()
                        .First()
                        .PairWithKey(member))
                .First(
                    (partitionKeyMember, next) =>
                    {
                        var pk = partitionKeyMember.Value
                            .GeneratePartitionKey(rowKey: rowKey, entity, partitionKeyMember.Key);
                        return (true, pk);
                    },
                    () =>
                    {
                        return (false, "");
                    });
            return success;
        }

        public static TEntity StorageParsePartitionKey<TEntity>(this TEntity entity, string partitionKey)
        {
            return (TEntity)entity.StorageParsePartitionKeyForType(partitionKey, typeof(TEntity));
        }

        public static object StorageParsePartitionKeyForType(this object entity, string partitionKey, Type type)
        {
            var partitionKeyMember = type
                .GetPropertyOrFieldMembers()
                .Where(member => member.ContainsAttributeInterface<EastFive.Persistence.IModifyAzureStorageTablePartitionKey>())
                .Select(member =>
                    member.GetAttributesInterface<EastFive.Persistence.IModifyAzureStorageTablePartitionKey>()
                        .First()
                        .PairWithKey(member))
                .First();
            return partitionKeyMember.Value.ParsePartitionKey(entity, partitionKey, partitionKeyMember.Key);
        }

        public static IEnumerable<string> StorageGetPartitionKeys(this Type type, int skip, int top)
        {
            var partitionKeyMember = type
                .GetPropertyOrFieldMembers()
                .Where(member => member.ContainsAttributeInterface<EastFive.Persistence.IComputeAzureStorageTablePartitionKey>())
                .Select(member =>
                    member.GetAttributesInterface<EastFive.Persistence.IComputeAzureStorageTablePartitionKey>()
                        .First()
                        .PairWithKey(member))
                .First();
            return partitionKeyMember.Value.GeneratePartitionKeys(type, skip: skip, top: top);
        }

        #endregion

        #region Metadata

        public static Task<StorageTables.TableInformation> StorageTableInformationAsync(this Type entityType,
            CloudTable table = default,
            string tableName = default,
            int numberOfTimesToRetry = AzureTableDriverDynamic.DefaultNumberOfTimesToRetry)
        {
            var driver = AzureTableDriverDynamic.FromSettings();
            var tableInformationTaskObj = typeof(AzureTableDriverDynamic)
                .GetMethod(nameof(AzureTableDriverDynamic.TableInformationAsync), BindingFlags.Instance | BindingFlags.Public)
                .MakeGenericMethod(entityType.AsArray())
                .Invoke(driver, new object[] { table, tableName, numberOfTimesToRetry, default(CancellationToken) });

            var tableInformationTask = tableInformationTaskObj as Task<StorageTables.TableInformation>;
            return tableInformationTask;
        }

        #endregion

        #region QUERY / GET

        public static IEnumerableAsync<object> StorageGetAll(this Type type, string tableName = default)
        {
            var findAllMethod = typeof(StorageExtensions)
                .GetMethod("StorageGetAllInternal", BindingFlags.Public | BindingFlags.Static);
            var findAllCast = findAllMethod.MakeGenericMethod(type.AsArray());
            return (IEnumerableAsync<object>)findAllCast.Invoke(null, new object[] { tableName });
        }

        public static IEnumerableAsync<object> StorageGetAllInternal<TEntity>(string tableName = default)
        {
            var driver = AzureTableDriverDynamic.FromSettings();
            Expression<Func<TEntity, bool>> expr = e => true;
            return driver
                .FindAll(expr, tableName: tableName)
                .Select(doc => (object)doc);
        }

        public static IEnumerableAsync<(TEntity, string)> StorageGetSegmented<TEntity>(this IQueryable<TEntity> entities,
            TableContinuationToken token = default,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var driver = AzureTableDriverDynamic.FromSettings();
            return driver.FindBySegmented(entities, token,
                cancellationToken:cancellationToken);
        }

        public static Task<TResult> StorageGetAsync<TEntity, TResult>(this Guid resourceId,
            Func<TEntity, TResult> onFound,
            Func<TResult> onDoesNotExists = default(Func<TResult>),
            Func<string> getPartitionKey = default(Func<string>))
            where TEntity : IReferenceable
        {
            return resourceId
                .AsRef<TEntity>()
                .StorageGetAsync(
                    onFound,
                    onDoesNotExists);
        }

        public static async Task<TResult> StorageGetAsync<TEntity, TResult>(this IRefOptional<TEntity> entityRefMaybe,
            Func<TEntity, TResult> onFound,
            Func<TResult> onDoesNotExists = default)
            where TEntity : IReferenceable
        {
            if (!entityRefMaybe.HasValue)
                return onDoesNotExists();
            return await entityRefMaybe.Ref.StorageGetAsync(
                onFound,
                onDoesNotExists: onDoesNotExists);
        }

        public static async Task<TResult> StorageGetAsync<TEntity, TResult>(this IRef<TEntity> entityRef,
            Func<TEntity, TResult> onFound,
            Func<TResult> onDoesNotExists = default,
            ICacheEntites cache = default)
            where TEntity : IReferenceable
        {
            if (entityRef.IsDefaultOrNull())
                return onDoesNotExists();
            var rowKey = entityRef.StorageComputeRowKey();
            var partitionKey = entityRef.StorageComputePartitionKey(rowKey);
            return await AzureTableDriverDynamic
                .FromSettings()
                .FindByIdAsync(rowKey, partitionKey,
                    onFound:(TEntity entity, TableResult tableResult) => onFound(entity),
                    onNotFound: onDoesNotExists,
                    cache:cache);
        }

        public static async Task<TResult> StorageGetAsync<TEntity, TResult>(this IRef<TEntity> entityRef,
            Func<IQueryable<TEntity>, IQueryable<TEntity>> additionalProperties,
            Func<TEntity, TResult> onFound,
            Func<TResult> onDoesNotExists = default)
            where TEntity : IReferenceable
        {
            if (entityRef.IsDefaultOrNull())
                return onDoesNotExists();
            var rowKey = entityRef.StorageComputeRowKey();

            var storageDriver = AzureTableDriverDynamic.FromSettings();
            var query = new StorageQuery<TEntity>(storageDriver);
            var queryById = query.StorageQueryById(entityRef);
            var queryFull = additionalProperties(queryById);
            var partitionKey = queryFull.StorageComputePartitionKey(rowKey);
            return await AzureTableDriverDynamic
                .FromSettings()
                .FindByIdAsync(rowKey, partitionKey,
                    onFound: (TEntity entity, TableResult tableResult) => onFound(entity),
                    onNotFound: onDoesNotExists);
        }

        public static async Task<TResult> StorageGetAsync<TEntity, TResult>(this IRefOptional<TEntity> entityRefMaybe,
                Func<IQueryable<TEntity>, IQueryable<TEntity>> additionalProperties,
            Func<TEntity, TResult> onFound,
            Func<TResult> onDoesNotExists = default)
            where TEntity : IReferenceable
        {
            if (!entityRefMaybe.HasValue)
                return onDoesNotExists();
            return await entityRefMaybe.Ref.StorageGetAsync(additionalProperties,
                onFound,
                onDoesNotExists: onDoesNotExists);
        }

        public static IEnumerableAsync<TEntity> StorageGet<TEntity>(this IQueryable<TEntity> entityQuery,
            System.Threading.CancellationToken cancellationToken = default)
            where TEntity : IReferenceable, new()
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .FindBy<TEntity>(entityQuery, tableName:default, cancellationToken: cancellationToken);
        }

        public static IEnumerableAsync<TEntity> StorageGetByIdProperty<TRefEntity, TEntity>(this IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, IRef<TRefEntity>>> idProperty)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .FindBy(entityRef, idProperty);
        }

        //public static IEnumerableAsync<TEntity> StorageGetBy<TRefEntity, TEntity>(this IRef<TRefEntity> entityRef,
        //        Expression<Func<TEntity, IRef<TRefEntity>>> idProperty,
        //        Expression<Func<TEntity, bool>> query1 = default,
        //        Expression<Func<TEntity, bool>> query2 = default)
        //    where TEntity : IReferenceable
        //    where TRefEntity : IReferenceable
        //{
        //    return AzureTableDriverDynamic
        //        .FromSettings()
        //        .FindBy(entityRef, idProperty, query1, query2);
        //}

        public static IEnumerableAsync<TEntity> StorageGetBy<TProperty, TEntity>(this TProperty propertyValue,
                Expression<Func<TEntity, TProperty>> propertyExpr,
                Expression<Func<TEntity, bool>> query1 = default,
                Expression<Func<TEntity, bool>> query2 = default,
                int readAhead = -1,
                ILogger logger = default)
            where TEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .FindBy(propertyValue, propertyExpr, query1, query2,
                    readAhead: readAhead,
                    logger:logger);
        }

        public static IEnumerableAsync<IRef<TEntity>> StorageGetIdsBy<TProperty, TEntity>(this TProperty propertyValue,
                Expression<Func<TEntity, TProperty>> propertyExpr,
                Expression<Func<TEntity, bool>> query1 = default,
                Expression<Func<TEntity, bool>> query2 = default,
                ILogger logger = default)
            where TEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .FindIdsBy(propertyValue, propertyExpr, logger: logger,
                    query1, query2)
                .Select(
                    refAst =>
                    {
                        var entity = Activator.CreateInstance<TEntity>();
                        var entityId = entity
                            .StorageParseRowKey(refAst.RowKey)
                            .StorageParsePartitionKey(refAst.PartitionKey)
                            .id;
                        return (IRef<TEntity>) new Ref<TEntity>(entityId);
                    });
        }

        public static Task<TResult> StorageGetLastModifiedBy<TProperty, TEntity, TResult>(this TProperty propertyValue,
                Expression<Func<TEntity, TProperty>> propertyExpr,
                Expression<Func<TEntity, bool>> query1 = default,
                Expression<Func<TEntity, bool>> query2 = default,
            Func<string, DateTime, int, TResult> onEtagLastModifedFound = default,
            Func<TResult> onNoLookupInfo = default)
            where TEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .FindModifiedByAsync(propertyValue, propertyExpr,
                    new[] { query1, query2 },
                    onEtagLastModifedFound,
                    onNoLookupInfo);
        }

        public static IEnumerableAsync<TEntity> StorageGetByIdProperty<TRefEntity, TEntity>(this IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, IRefOptional<TRefEntity>>> idProperty)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .FindBy(entityRef, idProperty);
        }

        public static IEnumerableAsync<TEntity> StorageGetByProperty<TRefEntity, TEntity>(this IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, IRef<IReferenceable>>> idProperty)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .FindBy(entityRef, idProperty);
        }

        public static IEnumerableAsync<TEntity> StorageGetByIdProperty<TEntity>(this Guid entityId,
                Expression<Func<TEntity, Guid>> idProperty,
                Expression<Func<TEntity, bool>> query1 = default,
                Expression<Func<TEntity, bool>> query2 = default)
            where TEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .FindBy(entityId, idProperty, query1, query2);
        }

        public static IEnumerableAsync<TEntity> StorageGetByIdsProperty<TRefEntity, TEntity>(this IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, IRefs<TRefEntity>>> idsProperty)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .FindBy(entityRef, idsProperty);
        }

        public static Task<TResult> StorageGetAsync<TEntity, TResult>(this IRefOptional<TEntity> entityRefMaybe,
            Func<TEntity, TResult> onFound,
            Func<TResult> onDoesNotExists = default,
            ICacheEntites cache = default)
            where TEntity : struct, IReferenceable
        {
            if (!entityRefMaybe.HasValueNotNull())
                return onDoesNotExists().AsTask();

            var entityRef = entityRefMaybe.Ref;
            return StorageGetAsync(entityRef,
                onFound,
                onDoesNotExists,
                cache:cache);
        }

        public static IEnumerableAsync<TEntity> StorageGet<TEntity>(this IRefs<TEntity> entityRefs, int? readAhead = default)
            where TEntity : IReferenceable
        {
            var partitionMember = typeof(TEntity).GetMembers()
                .Where(member => member.ContainsAttributeInterface<EastFive.Persistence.IComputeAzureStorageTablePartitionKey>())
                .First();
            var partitionGenerator = partitionMember
                .GetAttributeInterface<EastFive.Persistence.IComputeAzureStorageTablePartitionKey>();
            var keys = entityRefs.refs
                .Select(
                    r =>
                    {
                        var rowKey = r.StorageComputeRowKey();
                        var partition = partitionGenerator.ComputePartitionKey(r, partitionMember, rowKey);
                        return rowKey.AsAstRef(partition);
                    })
                .ToArray();
            return AzureTableDriverDynamic
                .FromSettings()
                .FindByIdsAsync<TEntity>(keys, readAhead: readAhead);
        }

        public static IEnumerableAsync<TEntity> StorageGet<TEntity>(this IRefs<TEntity> entityRefs,
                Func<IQueryable<TEntity>, IQueryable<TEntity>> additionalProperties,
                int? readAhead = default)
            where TEntity : IReferenceable
        {
            var partitionMember = typeof(TEntity).GetMembers()
                .Where(member => member.ContainsAttributeInterface<EastFive.Persistence.IComputeAzureStorageTablePartitionKey>())
                .First();
            var partitionGenerator = partitionMember
                .GetAttributeInterface<EastFive.Persistence.IComputeAzureStorageTablePartitionKey>();
            var storageDriver = AzureTableDriverDynamic.FromSettings();
            var keys = entityRefs.refs
                .Select(
                    entityRef =>
                    {
                        var rowKey = entityRef.StorageComputeRowKey();

                        var query = new StorageQuery<TEntity>(storageDriver);
                        var queryById = query.StorageQueryById(entityRef);
                        var queryFull = additionalProperties(queryById);
                        var partitionKey = queryFull.StorageComputePartitionKey(rowKey);

                        return rowKey.AsAstRef(partitionKey);
                    })
                .ToArray();
            return AzureTableDriverDynamic
                .FromSettings()
                .FindByIdsAsync<TEntity>(keys, readAhead: readAhead);
        }

        public static IEnumerableAsync<TEntity> StorageQuery<TEntity>(
            this Expression<Func<TEntity, bool>> query,
            ICacheEntites cache = default)
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .FindAll(query, cache: cache);
        }

        public static IEnumerableAsync<TEntity> StorageGetPartition<TEntity>(this string partition)
            where TEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .FindEntityBypartition<TEntity>(partition);
        }

        public static IEnumerableAsync<TEntity> StorageFindbyQuery<TEntity>(
            this string whereClause,
            ICacheEntites cache = default)
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .FindByQuery<TEntity>(whereClause, cache: cache);
        }

        #endregion

        #region Dictionary

        public static IEnumerableAsync<IDictionary<string, TValue>> StorageGetPartitionAsDictionary<TValue>(
            this string partition, string tableName)
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .FindByPartition<DictionaryTableEntity<TValue>>(partition,
                    tableName)
                .Select(dictTableEntity => dictTableEntity.values);
        }

        public static ITableEntity ToTableEntity<TValue>(this IDictionary<string, TValue> dictionary,
            string rowKey, string partitionKey)
        {
            return new EastFive.Persistence.Azure.StorageTables.DictionaryTableEntity<TValue>(
                rowKey, partitionKey, dictionary);
        }

        //[Obsolete("Use string row/partition keys")]
        //public static ITableEntity ToTableEntity<TValue>(this IDictionary<string, TValue> dictionary,
        //    Guid id)
        //{
        //    var key = id.AsRowKey();
        //    var partition = key.GeneratePartitionKey();
        //    return new EastFive.Persistence.Azure.StorageTables.DictionaryTableEntity<TValue>(
        //        key, partition, dictionary);
        //}

        public static IEnumerableAsync<TableResult> StorageQueryDelete<TEntity>(
            this Expression<Func<TEntity, bool>> query)
            where TEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .DeleteAll(query);
        }

        #endregion

        #region Create

        public static Task<TResult> StorageCreateAsync<TEntity, TResult>(this TEntity entity,
            Func<EastFive.Persistence.Azure.StorageTables.IAzureStorageTableEntity<TEntity>, TResult> onCreated,
            Func<TResult> onAlreadyExists = default,
            params IHandleFailedModifications<TResult>[] onModificationFailures)
            where TEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .CreateAsync(entity,
                    onSuccess: (entity, tr) => onCreated(entity),
                    onAlreadyExists:onAlreadyExists,
                    onModificationFailures: onModificationFailures);
        }

        #endregion

        #region CreateOrUpdate

        public static Task<TResult> StorageCreateOrUpdateAsync<TEntity, TResult>(this IRef<TEntity> entityRef,
            Func<bool, TEntity, Func<TEntity, Task<ITableResult>>, Task<TResult>> onCreated,
            params IHandleFailedModifications<TResult>[] onModificationFailures)
            where TEntity : IReferenceable
        {
            var rowKey = entityRef.StorageComputeRowKey();
            var partitionKey = entityRef.StorageComputePartitionKey(rowKey);
            return AzureTableDriverDynamic
                .FromSettings()
                .UpdateOrCreateAsync<TEntity, TResult>(rowKey, partitionKey,
                    onUpdate:
                        (created, entity, callback) =>
                        {
                            return onCreated(created, entity,
                                async (entityToSave) =>
                                {
                                    var tr = await callback(entityToSave);
                                    return new StorageTableResult(tr);
                                });
                        },
                    default);
        }

        public static Task<TResult> StorageCreateOrUpdateAsync<TEntity, TResult>(this IRef<TEntity> entityRef,
                Func<IQueryable<TEntity>, IQueryable<TEntity>> additionalProperties,
            Func<bool, TEntity, Func<TEntity, Task>, Task<TResult>> onCreated,
            params IHandleFailedModifications<TResult>[] onModificationFailures)
            where TEntity : IReferenceable
        {
            var rowKey = entityRef.StorageComputeRowKey();
            var storageDriver = AzureTableDriverDynamic.FromSettings();
            var query = new StorageQuery<TEntity>(storageDriver);
            var queryById = query.StorageQueryById(entityRef);
            var queryFull = additionalProperties(queryById);
            var partitionKey = queryFull.StorageComputePartitionKey(rowKey);
            return AzureTableDriverDynamic
                .FromSettings()
                .UpdateOrCreateAsync<TEntity, TResult>(rowKey, partitionKey,
                    onCreated,
                    default);
        }

        public static Task<TResult> StorageCreateOrUpdateAsync<TEntity, TResult>(this IQueryable<TEntity> entityQuery,
            Func<bool, TEntity, Func<TEntity, Task>, Task<TResult>> onCreated,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            params IHandleFailedModifications<TResult>[] onModificationFailures)
            where TEntity : IReferenceable
        {
            var rowKey = entityQuery.StorageComputeRowKey();
            var partitionKey = entityQuery.StorageComputePartitionKey(rowKey);
            return AzureTableDriverDynamic
                .FromSettings()
                .UpdateOrCreateAsync(rowKey, partitionKey,
                    onCreated,
                    onModificationFailures: onModificationFailures);
        }

        [Obsolete]
        public static Task<TResult> StorageCreateOrUpdateAsync<TEntity, TResult>(this IRef<TEntity> entityRef,
            string partitionKey,
            Func<bool, TEntity, Func<TEntity, Task>, Task<TResult>> onCreated)
            where TEntity : struct, IReferenceable
        {
            var rowKey = entityRef.StorageComputeRowKey();
            return AzureTableDriverDynamic
                .FromSettings()
                .UpdateOrCreateAsync<TEntity, TResult>(rowKey, partitionKey,
                    onCreated,
                    onTimeoutAsync: default(AzureStorageDriver.RetryDelegateAsync<Task<TResult>>));
        }

        public static Task<TResult> StorageInsertOrReplaceAsync<TEntity, TResult>(this TEntity entity,
            Func<bool, TResult> onSuccess,
            IHandleFailedModifications<TResult>[] onModificationFailures = default,
            Func<StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = null,
            AzureStorageDriver.RetryDelegate onTimeout = null)
            where TEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .InsertOrReplaceAsync(entity,
                    onSuccess,
                    onModificationFailures: onModificationFailures,
                    onFailure: onFailure,
                    onTimeout: onTimeout);
        }

        //public static Task<TResult> StorageInsertOrReplaceAsync<TEntity, TResult>(this TEntity entity,
        //    Func<bool, TResult> onSuccess,
        //    IHandleFailedModifications<TResult>[] onModificationFailures = default,
        //    Func<StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = null,
        //    AzureStorageDriver.RetryDelegate onTimeout = null)
        //    where TEntity : IReferenceable
        //{
        //    var driver = AzureTableDriverDynamic
        //        .FromSettings();
        //    driver.CreateAsync(entity,
        //        tableEntity => onSuccess(true),
        //        onAlreadyExists:
        //            () =>
        //            {
        //                var rowKey = entity.StorageGetRowKey();
        //                var partitionKey = entity.StorageGetPartitionKey();
        //                driver.UpdateOrCreateAsync(rowKey, partitionKey,);
        //            });
        //        .InsertOrReplaceAsync(entity,
        //            onSuccess,
        //            onModificationFailures: onModificationFailures,
        //            onFailure: onFailure,
        //            onTimeout: onTimeout);
        //}

        public static IEnumerableAsync<TResult> StorageCreateOrUpdateBatch<TEntity, TResult>(this IEnumerable<TEntity> entities,
            Func<TEntity, TableResult, TResult> perItemCallback,
            string tableName = default(string),
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = default,
            EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger))
            where TEntity : class, ITableEntity
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .CreateOrUpdateBatch<TResult>(entities,
                    (tableEntity, result) =>
                    {
                        var entity = tableEntity as TEntity;
                        return perItemCallback(entity, result);
                    },
                    tableName: tableName,
                    onTimeout: onTimeout,
                    diagnostics: diagnostics);
        }

        public static IEnumerableAsync<TResult> StorageCreateOrUpdateBatch<TEntity, TResult>(this IEnumerableAsync<TEntity> entities,
            Func<TEntity, TableResult, TResult> perItemCallback,
            string tableName = default(string),
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = default,
            EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger))
            where TEntity : class, ITableEntity
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .CreateOrUpdateBatch<TResult>(entities,
                    (tableEntity, result) =>
                    {
                        var entity = tableEntity as TEntity;
                        return perItemCallback(entity, result);
                    },
                    tableName: tableName,
                    onTimeout: onTimeout,
                    diagnostics: diagnostics);
        }

        public static IEnumerableAsync<TResult> StorageCreateOrReplaceBatch<TEntity, TResult>(
            this IEnumerableAsync<TEntity> entities,
            Func<TEntity, TableResult, TResult> perItemCallback,
            string tableName = default(string),
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = default,
            EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger))
            where TEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .CreateOrReplaceBatch<TEntity, TResult>(entities,
                    (tableEntity, result) =>
                    {
                        var entity = (result.Result as IAzureStorageTableEntity<TEntity>).Entity;
                        return perItemCallback(entity, result);
                    },
                    tableName: tableName,
                    onTimeout: onTimeout,
                    diagnostics: diagnostics);
        }

        #endregion

        #region Update

        public static Task<TResult> StorageReplaceAsync<TEntity, TResult>(this TEntity entity,
            Func<TResult> onSuccess,
            Func<StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = null,
            AzureStorageDriver.RetryDelegate onTimeout = null)
            where TEntity : IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .ReplaceAsync(entity,
                    onSuccess,
                    onFailure: onFailure,
                    onTimeout: onTimeout);
        }

        public static Task<TResult> StorageUpdateAsync<TEntity, TResult>(this IRef<TEntity> entityRef,
            Func<TEntity, Func<TEntity, Task<IUpdateTableResult>>, Task<TResult>> onUpdate,
            Func<TResult> onNotFound = default,
            IHandleFailedModifications<TResult>[] onModificationFailures = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeoutAsync =
                default(Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegateAsync<Task<TResult>>))
            where TEntity : IReferenceable
        {
            var rowKey = entityRef.StorageComputeRowKey();
            return AzureTableDriverDynamic
                .FromSettings()
                .UpdateAsync<TEntity, TResult>(
                        rowKey,
                        entityRef.StorageComputePartitionKey(rowKey),
                    onUpdate:
                        (entity, callback) =>
                        {
                            return onUpdate(entity,
                                async (entityToSave) =>
                                {
                                    var tr = await callback(entityToSave);
                                    return new StorageUpdateTableResult(tr);
                                });
                        },
                    onNotFound: onNotFound,
                    onModificationFailures: onModificationFailures,
                    onTimeoutAsync: onTimeoutAsync);
        }

        public static Task<TResult> StorageUpdateAsyncAsync<TEntity, TResult>(this IRef<TEntity> entityRef,
            Func<TEntity, Func<TEntity, Task<ITableResult>>, Task<TResult>> onUpdate,
            Func<Task<TResult>> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, Task<TResult>> onFailure = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeoutAsync =
                default(Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegateAsync<Task<TResult>>))
            where TEntity : struct, IReferenceable
        {
            var rowKey = entityRef.StorageComputeRowKey();
            return AzureTableDriverDynamic
                .FromSettings()
                .UpdateAsyncAsync<TEntity, TResult>(rowKey, entityRef.StorageComputePartitionKey(rowKey),
                    onUpdate:
                        (entity, callback) =>
                        {
                            return onUpdate(entity,
                                async (entityToSave) =>
                                {
                                    var tr = await callback(entityToSave);
                                    return new StorageTableResult(tr);
                                });
                        },
                    onNotFound: onNotFound,
                    onTimeoutAsync: onTimeoutAsync);
        }

        #endregion

        #region Delete

        public static Task<TResult> StorageDeleteAsync<TEntity, TResult>(this IRef<TEntity> entityRef,
            Func<TResult> onSuccess,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            where TEntity : IReferenceable
        {
            var rowKey = entityRef.StorageComputeRowKey();
            return AzureTableDriverDynamic
                .FromSettings()
                .DeleteAsync<TEntity, TResult>(
                        rowKey,
                        entityRef.StorageComputePartitionKey(rowKey),
                    (discard) => onSuccess(),
                    onNotFound,
                    onFailure);
        }

        public static Task<TResult> StorageDeleteAsync<TEntity, TResult>(this IRef<TEntity> entityRef,
            Func<TEntity, TResult> onSuccess,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            where TEntity : IReferenceable
        {
            var rowKey = entityRef.StorageComputeRowKey();
            return AzureTableDriverDynamic
                .FromSettings()
                .DeleteAsync<TEntity, TResult>(
                        rowKey,
                        entityRef.StorageComputePartitionKey(rowKey),
                    onSuccess,
                    onNotFound,
                    onFailure);
        }

        public static Task<TResult> StorageDeleteIfAsync<TEntity, TResult>(this IRef<TEntity> entityRef,
            Func<TEntity, Func<Task>, Task<TResult>> onFound,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            where TEntity : IReferenceable
        {
            var rowKey = entityRef.StorageComputeRowKey();
            var partitionKey = entityRef.StorageComputePartitionKey(rowKey);
            return AzureTableDriverDynamic
                .FromSettings()
                .DeleteAsync<TEntity, TResult>(
                        rowKey, partitionKey,
                    (entity, deleteAsync) => onFound(entity, () => deleteAsync()),
                    onNotFound, 
                    onFailure);
        }

        public static Task<TResult> StorageDeleteIfAsync<TEntity, TResult>(this IQueryable<TEntity> entityQuery,
            Func<TEntity, Func<Task>, Task<TResult>> onFound,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            where TEntity : IReferenceable
        {
            var rowKey = entityQuery.StorageComputeRowKey();
            var partitionKey = entityQuery.StorageComputePartitionKey(rowKey);
            return AzureTableDriverDynamic
                .FromSettings()
                .DeleteAsync<TEntity, TResult>(
                        rowKey, partitionKey,
                    (entity, deleteAsync) => onFound(entity, () => deleteAsync()),
                    onNotFound,
                    onFailure);
        }

        public static Task<TResult> StorageDeleteAsync<TEntity, TResult>(this IRef<TEntity> entityRef,
                string partitionKey,
            Func<TResult> onSuccess,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            where TEntity : struct, IReferenceable
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .DeleteAsync<TEntity, TResult>(
                        entityRef.StorageComputeRowKey(),
                        partitionKey,
                    (discard) => onSuccess(),
                    onNotFound,
                    onFailure);
        }

        public static IEnumerableAsync<TResult> StorageDeleteBatch<TEntity, TResult>(this IEnumerableAsync<IRef<TEntity>> entityRefs,
            Func<TableResult, TResult> onSuccess)
            where TEntity : IReferenceable
        {
            var documentIds = entityRefs.Select(entity => entity.id);
            return AzureTableDriverDynamic
                .FromSettings()
                .DeleteBatch<TEntity, TResult>(documentIds, onSuccess);
        }

        public static IEnumerableAsync<TResult> StorageDeleteBatch<TEntity, TResult>(this IEnumerableAsync<TEntity> entities,
            Func<TableResult, TResult> onSuccess)
            where TEntity : IReferenceable
        {
            var documentIds = entities.Select(entity => entity.id);
            return AzureTableDriverDynamic
                .FromSettings()
                .DeleteBatch<TEntity, TResult>(documentIds, onSuccess);
        }

        public static IEnumerableAsync<TResult> StorageDeleteBatch<TEntity, TResult>(this IEnumerable<TEntity> entities,
            Func<TableResult, TResult> onSuccess)
            where TEntity : IReferenceable
        {
            var documentIds = entities.Select(entity => entity.id);
            return AzureTableDriverDynamic
                .FromSettings()
                .DeleteBatch<TEntity, TResult>(documentIds, onSuccess);
        }

        #endregion

        #region Locks

        public static Task<TResult> StorageLockedUpdateAsync<TEntity, TResult>(this IRef<TEntity> entityRef,
                Expression<Func<TEntity, DateTime?>> lockedPropertyExpression,
            AzureTableDriverDynamic.WhileLockedDelegateAsync<TEntity, TResult> onLockAquired,
            Func<TResult> onNotFound,
            Func<TResult> onLockRejected = default(Func<TResult>),
            AzureTableDriverDynamic.ContinueAquiringLockDelegateAsync<TEntity, TResult> onAlreadyLocked = default,
            AzureTableDriverDynamic.ConditionForLockingDelegateAsync<TEntity, TResult> shouldLock = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeout = default,
            Func<TEntity, TEntity> mutateUponLock = default)
            where TEntity : IReferenceable
        {
            var rowKey = entityRef.StorageComputeRowKey();
            return AzureTableDriverDynamic
                .FromSettings()
                .LockedUpdateAsync(
                        rowKey,
                        entityRef.StorageComputePartitionKey(rowKey),
                        lockedPropertyExpression,
                    onLockAquired,
                    onNotFound: onNotFound,
                    onLockRejected: onLockRejected,
                    onAlreadyLocked: onAlreadyLocked,
                    shouldLock: shouldLock,
                    onTimeout: onTimeout,
                    mutateUponLock: mutateUponLock);
        }

        public static Task<TResult> StorageLockedUpdateAsync<TEntity, TResult>(this IQueryable<TEntity> entityQuery,
                Expression<Func<TEntity, DateTime?>> lockedPropertyExpression,
            AzureTableDriverDynamic.WhileLockedDelegateAsync<TEntity, TResult> onLockAquired,
            Func<TResult> onNotFound,
            Func<TResult> onLockRejected = default(Func<TResult>),
            AzureTableDriverDynamic.ContinueAquiringLockDelegateAsync<TEntity, TResult> onAlreadyLocked = default,
            AzureTableDriverDynamic.ConditionForLockingDelegateAsync<TEntity, TResult> shouldLock = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeout = default,
            Func<TEntity, TEntity> mutateUponLock = default)
            where TEntity : IReferenceable
        {
            var rowKey = entityQuery.StorageComputeRowKey();
            var partitionKey = entityQuery.StorageComputePartitionKey(rowKey);
            return AzureTableDriverDynamic
                .FromSettings()
                .LockedUpdateAsync(
                        rowKey, partitionKey,
                        lockedPropertyExpression,
                    onLockAquired,
                    onNotFound: onNotFound,
                    onLockRejected: onLockRejected,
                    onAlreadyLocked: onAlreadyLocked,
                    shouldLock: shouldLock,
                    onTimeout: onTimeout,
                    mutateUponLock: mutateUponLock);
        }

        #endregion

        #region Transactions

        public static async Task<ITransactionResult<TResult>> CheckAsync<T, TResult>(this IRef<T> value,
            Func<TResult> onNotFound)
            where T : struct, IReferenceable
        {
            if (value.IsDefaultOrNull())
                return onNotFound().TransactionResultFailure();

            return await value.StorageGetAsync(
                valueValue =>
                {
                    Func<Task> rollback = () => 1.AsTask();
                    return rollback.TransactionResultSuccess<TResult>();
                },
                () => onNotFound().TransactionResultFailure());
        }

        public static async Task<ITransactionResult<TResult>> TransactionUpdateLinkN1Async<T, TLink, TResult>(this T value,
            Func<T, IRefOptional<TLink>> linkedOutOptional,
            Expression<Func<TLink, IRefs<T>>> linkedBack,
            Func<TResult> onNotFound)
            where T : struct, IReferenceable where TLink : struct, IReferenceable
        {
            var refOptional = linkedOutOptional(value);
            if(!refOptional.HasValue)
            {
                Func<Task> rollbackValues =
                        () => true.AsTask();
                return rollbackValues.TransactionResultSuccess<TResult>();
            }
            var linkedOut = refOptional.Ref;
            return await value.TransactionUpdateLinkN1Async(
                (res) => linkedOut,
                linkedBack,
                onNotFound);
        }

        public static Task<ITransactionResult<TResult>> TransactionUpdateLinkN1Async<T, TLink, TResult>(this T value,
            Func<T, IRef<TLink>> linkedOut,
            Expression<Func<TLink, IRefs<T>>> linkedBack,
            Func<TResult> onNotFound)
            where T : struct, IReferenceable where TLink : struct, IReferenceable
        {
            var driver = AzureTableDriverDynamic
                   .FromSettings();

            var linkRef = linkedOut(value);
            if (linkRef.IsDefaultOrNull())
                return onNotFound().TransactionResultFailure().AsTask();

            var xmemberExpr = (linkedBack.Body as MemberExpression);
            if (xmemberExpr.IsDefaultOrNull())
                throw new Exception($"`{linkedBack.Body}` is not a member expression");
            var memberInfo = xmemberExpr.Member;
            return linkRef.StorageUpdateAsync(
                async (linkedValue, updateAsync) =>
                {
                    var linkRefsOld = (IRefs<T>)memberInfo.GetValue(linkedValue);

                    if (linkRefsOld.ids.Contains(value.id))
                    {
                        Func<Task> rollback = () => 1.AsTask();
                        return rollback.TransactionResultSuccess<TResult>();
                    }

                    var linkIdsNew = linkRefsOld.ids.Append(value.id).ToArray();
                    var linkRefsNew = new Refs<T>(linkIdsNew);
                    memberInfo.SetValue(ref linkedValue, linkRefsNew);
                    await updateAsync(linkedValue);

                    Func<Task> rollbackValues = 
                        () => linkRef.StorageUpdateAsync(
                            async (linkedValueRollback, updateAsyncRollback) =>
                            {
                                var linkRefsOldRollback = (IRefs<T>)memberInfo.GetValue(linkedValueRollback);
                                if (linkRefsOld.ids.Contains(value.id))
                                    return false;

                                var linkIdsNewRollback = linkRefsOldRollback.ids.Where(id => id != value.id).ToArray();
                                var linkRefsNewRollback = new Refs<T>(linkIdsNewRollback);
                                memberInfo.SetValue(ref linkedValueRollback, linkRefsNewRollback);
                                await updateAsyncRollback(linkedValueRollback);
                                return true;
                            },
                            () => false);
                    return rollbackValues.TransactionResultSuccess<TResult>();
                },
                () => onNotFound().TransactionResultFailure());
            
        }

        public static async Task<ITransactionResult<TResult>> StorageCreateTransactionAsync<TEntity, TResult>(this TEntity entity,
            Func<TResult> onAlreadyExists) 
            where TEntity : IReferenceable
        {
            var driver = AzureTableDriverDynamic
                .FromSettings();
            return await driver
                .CreateAsync(entity,
                    (tableEntity, tableResult) =>
                    {
                        Func<Task> rollback = (() =>
                        {
                            return driver.DeleteAsync<TEntity, bool>(tableEntity.RowKey, tableEntity.PartitionKey,
                                (discard) => true,
                                () => false);
                        });
                        return rollback.TransactionResultSuccess<TResult>();
                    },
                    () => onAlreadyExists().TransactionResultFailure());
        }

        #endregion

        #region BLOB

        public static Task<Guid> BlobCreateAsync(this byte[] content, string containerName,
            string contentType = default,
            IDictionary<string, string> metadata = default,
            AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            var blobId = Guid.NewGuid();
            return content.BlobCreateAsync(blobId, containerName,
                () => blobId,
                () => throw new Exception("Guid not unique."),
                contentType: contentType,
                metadata: metadata,
                onTimeout: onTimeout);
        }

        public static Task<TResult> BlobCreateAsync<TResult>(this byte[] content, string containerName,
            Func<Guid, TResult> onSuccess = default,
            Func<StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default,
            IDictionary<string, string> metadata = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            var blobId = Guid.NewGuid();
            return content.BlobCreateAsync(blobId, containerName,
                () => onSuccess(blobId),
                () => throw new Exception("Guid not unique."),
                onFailure: onFailure,
                contentType: contentType,
                metadata: metadata,
                onTimeout: onTimeout);
        }

        public static Task<TResult> BlobCreateAsync<TResult>(this byte[] content, Guid blobId, string containerName,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists,
            Func<StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default,
            IDictionary<string, string> metadata = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .BlobCreateAsync(content, blobId, containerName,
                    onSuccess,
                    onAlreadyExists: onAlreadyExists,
                    onFailure: onFailure,
                    contentType: contentType,
                    metadata: metadata,
                    onTimeout: onTimeout);
        }

        public static Task<TResult> BlobCreateAsync<TResult>(this Stream content, Guid blobId, string containerName,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default,
            IDictionary<string, string> metadata = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .BlobCreateAsync(content, blobId, containerName,
                    onSuccess,
                    onAlreadyExists: onAlreadyExists,
                    onFailure: onFailure,
                    contentType: contentType,
                    metadata: metadata,
                    onTimeout: onTimeout);
        }

        public static Task<TResult> BlobCreateAsync<TResult>(this Guid blobId, string containerName,
                Func<Stream, Task> writeAsync,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default,
            IDictionary<string, string> metadata = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .BlobCreateAsync(blobId, containerName,
                        writeAsync,
                    onSuccess: onSuccess,
                    onAlreadyExists: onAlreadyExists,
                    onFailure: onFailure,
                    contentType: contentType,
                    metadata: metadata,
                    onTimeout: onTimeout);
        }

        public static async Task<TResult> BlobLoadBytesAsync<TResult>(this Guid? blobId, string containerName,
            Func<byte[], string, TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            if (!blobId.HasValue)
                return onNotFound();
            return await blobId.Value.BlobLoadBytesAsync(containerName, 
                onSuccess,
                onNotFound: onNotFound,
                onFailure: onFailure,
                onTimeout: onTimeout);
        }

        public static Task<TResult> BlobLoadBytesAsync<TResult>(this Guid blobId, string containerName,
            Func<byte [], string, TResult> onSuccess,
            Func<TResult> onNotFound = default,
            Func<StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .BlobLoadBytesAsync(blobId, containerName,
                    onSuccess,
                    onNotFound,
                    onFailure: onFailure,
                    onTimeout: onTimeout);
        }

        public static Task<TResult> BlobLoadStreamAsync<TResult>(this Guid blobId, string containerName,
            Func<System.IO.Stream, string, TResult> onSuccess,
            Func<TResult> onNotFound = default,
            Func<StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .BlobLoadStreamAsync(blobId, containerName,
                    onSuccess,
                    onNotFound,
                    onFailure: onFailure,
                    onTimeout: onTimeout);
        }

        public static Task<TResult> BlobLoadStreamAsync<TResult>(this Guid blobId, string containerName,
            Func<System.IO.Stream, string, IDictionary<string, string>, TResult> onSuccess,
            Func<TResult> onNotFound = default,
            Func<StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            return AzureTableDriverDynamic
                .FromSettings()
                .BlobLoadStreamAsync(blobId, containerName,
                    onSuccess,
                    onNotFound,
                    onFailure: onFailure,
                    onTimeout: onTimeout);
        }

        #endregion
    }
}
