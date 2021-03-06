﻿using BlackBarLabs;
using BlackBarLabs.Extensions;
using BlackBarLabs.Persistence.Azure.Attributes;
using BlackBarLabs.Persistence.Azure.StorageTables;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Linq.Expressions;
using EastFive.Persistence.Azure.StorageTables.Driver;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class CascadeDeleteAttribute : Attribute,
        IModifyAzureStorageTableSave, IPersistInAzureStorageTables 
    {
        public string Name { get; set; }

        public interface IDeleteCascaded
        {
            string Cascade { get; }

            Task<Func<Task>> CascadeDeleteAsync<TEntity>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository);
        }

        #region IModifyAzureStorageTableSave

        public Task<TResult> ExecuteCreateAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            return onSuccessWithRollback(() => true.AsTask()).AsTask();
        }

        public Task<TResult> ExecuteInsertOrReplaceAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef, TEntity value,
                IDictionary<string, EntityProperty> dictionary, 
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback, 
            Func<TResult> onFailure)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> ExecuteUpdateAsync<TEntity, TResult>(MemberInfo memberInfo, 
                string rowKeyRef, string partitionKeyRef, 
                TEntity valueExisting, IDictionary<string, EntityProperty> dictionaryExisting,
                TEntity valueUpdated, IDictionary<string, EntityProperty> dictionaryUpdated, 
                AzureTableDriverDynamic repository, 
            Func<Func<Task>, TResult> onSuccessWithRollback, 
            Func<TResult> onFailure)
        {
            return onSuccessWithRollback(() => true.AsTask()).AsTask();
        }

        public async Task<TResult> ExecuteDeleteAsync<TEntity, TResult>(MemberInfo memberInfo, 
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary, 
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback, 
            Func<TResult> onFailure)
        {
            var type = memberInfo.GetMemberType().GenericTypeArguments.First();
            var rollback = await type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(member => member.ContainsAttributeInterface<IDeleteCascaded>())
                .First<MemberInfo, Task<Func<Task>>>(
                    (member, next) =>
                    {
                        var cascadeAttr = member.GetAttributeInterface<IDeleteCascaded>();
                        if (cascadeAttr.Cascade != this.Name)
                            return next();
                        return cascadeAttr.CascadeDeleteAsync(member,
                            rowKeyRef, partitionKeyRef,
                            value, dictionary,
                            repository);
                    },
                    () => throw new Exception($"Cascade references property named {this.Name} on {type.FullName} which does not exists or does not contain attribute of type {typeof(IDeleteCascaded).FullName}."));
            return onSuccessWithRollback(rollback);
        }

        #endregion

        #region 

        public KeyValuePair<string, EntityProperty>[] ConvertValue(object value, MemberInfo memberInfo)
        {
            return new KeyValuePair<string, EntityProperty>[] { };
        }

        public object GetMemberValue(MemberInfo memberInfo, IDictionary<string, EntityProperty> values)
        {
            // TODO: Setup a projection for the StorageCall
            return memberInfo.GetMemberType().GetDefault();
        }

        public string GetTablePropertyName(MemberInfo member)
        {
            return $"CASCADE__{member.Name}";
        }

        #endregion
    }
}
