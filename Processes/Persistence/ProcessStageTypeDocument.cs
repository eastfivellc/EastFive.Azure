using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Azure.Cosmos.Table;

using EastFive.Collections.Generic;

using BlackBarLabs.Persistence;
using EastFive.Serialization;
using BlackBarLabs.Persistence.Azure;
using EastFive.Extensions;
using EastFive;
using EastFive.Linq;
using BlackBarLabs.Persistence.Azure.StorageTables;
using System.Runtime.Serialization;

namespace EastFive.Azure.Persistence
{
    public class ProcessStageTypeDocument : TableEntity
    {
        #region Properties

        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id => Guid.Parse(this.RowKey);
        
        public Guid Owner { get; set; }

        public Guid ProcessStageGroup { get; set; }

        public string Title { get; set; }

        public string ResourceType { get; set; }

        #region Resource Keys

        public byte[] ResourceKeysKeys { get; set; }

        public byte[] ResourceKeysValues { get; set; }

        internal KeyValuePair<string, Type>[] GetResourceKeys()
        {
            return ResourceKeysKeys
                .ToStringsFromUTF8ByteArray()
                .Zip(ResourceKeysValues.ToStringsFromUTF8ByteArray(),
                    (k, assemblyQualifiedName) => k.PairWithValue(Type.GetType(assemblyQualifiedName)))
                .ToArray();
        }

        internal bool SetResourceKeys(KeyValuePair<string, Type>[] resourceKeys)
        {
            this.ResourceKeysKeys = resourceKeys
                .SelectKeys()
                .ToUTF8ByteArrayOfStrings();
            this.ResourceKeysValues = resourceKeys
                .SelectValues(v => v.AssemblyQualifiedName)
                .ToUTF8ByteArrayOfStrings();
            return true;
        }

        #endregion

        #endregion

        internal static Task<TResult> FindAllAsync<TResult>(
            Func<ProcessStageType [], TResult> onFound)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository =>azureStorageRepository
                    .FindAllAsync(
                        (ProcessStageTypeDocument[] processStageDocuments) =>
                            onFound(processStageDocuments.Select(Convert).ToArray())));
        }

        internal static Task<TResult> FindByIdAsync<TResult>(Guid processStageTypeId,
            Func<ProcessStageType, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository => azureStorageRepository
                    .FindByIdAsync(processStageTypeId,
                        (ProcessStageTypeDocument processStageDocument) =>
                            onFound(Convert(processStageDocument)),
                        onNotFound));
        }

        internal static ProcessStageType Convert(ProcessStageTypeDocument processStageDocument)
        {
            return new ProcessStageType
            {
                processStageTypeId = processStageDocument.Id,
                processStageGroupId = processStageDocument.ProcessStageGroup,
                title = processStageDocument.Title,
                resourceType = Type.GetType(processStageDocument.ResourceType),
                resourceKeys = processStageDocument.GetResourceKeys(),
            };
        }

        internal static Task<TResult> CreateAsync<TResult>(Guid processStageTypeId, Guid actorId,
                Guid processStageGroupId, string title, Type resourceType, KeyValuePair<string, Type>[] resourceKeys,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository =>
                {
                    var procStageTypeDoc = new ProcessStageTypeDocument()
                    {
                        Owner = actorId,
                        ProcessStageGroup = processStageGroupId,
                        Title = title,
                        ResourceType = resourceType.AssemblyQualifiedName,
                    };
                    procStageTypeDoc.SetResourceKeys(resourceKeys);
                    return azureStorageRepository.CreateAsync(processStageTypeId, procStageTypeDoc,
                        onSuccess,
                        onAlreadyExists);
                });
        }
        
    }
}
