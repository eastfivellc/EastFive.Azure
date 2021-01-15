using System;
using System.Runtime.Serialization;
using Microsoft.Azure.Cosmos.Table;
using EastFive.Serialization;
using System.Linq;
using BlackBarLabs.Extensions;
using EastFive.Linq;
using EastFive.Collections.Generic;
using BlackBarLabs.Linq;
using System.Collections.Generic;
using BlackBarLabs.Persistence.Azure.Attributes;

namespace EastFive.Azure.Persistence.Documents
{
    [Serializable]
    [DataContract]
    [StorageResource(typeof(StandardPartitionKeyGenerator), typeof(OnePlaceHexadecimalKeyGenerator))]
    public class LookupDocument : TableEntity
    {
        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id => Guid.Parse(this.RowKey);
        
        public byte[] LookupDocumentIds_00 { get; set; }
        public byte[] LookupDocumentIds_01 { get; set; }
        public byte[] LookupDocumentIds_02 { get; set; }
        public byte[] LookupDocumentIds_03 { get; set; }
        public byte[] LookupDocumentIds_04 { get; set; }
        public byte[] LookupDocumentIds_05 { get; set; }
        public byte[] LookupDocumentIds_06 { get; set; }
        public byte[] LookupDocumentIds_07 { get; set; }
        public byte[] LookupDocumentIds_08 { get; set; }
        public byte[] LookupDocumentIds_09 { get; set; }
        public byte[] LookupDocumentIds_10 { get; set; }
        public byte[] LookupDocumentIds_11 { get; set; }
        public byte[] LookupDocumentIds_12 { get; set; }
        public byte[] LookupDocumentIds_13 { get; set; }
        public byte[] LookupDocumentIds_14 { get; set; }
        public byte[] LookupDocumentIds_15 { get; set; }

        public Guid[] GetLookupDocumentIds()
        {
            return typeof(LookupDocument)
                .GetProperties()
                .Where(property => property.Name.StartsWith("LookupDocumentIds_"))
                .SelectMany(property => ((byte[])property.GetValue(this)).ToGuidsFromByteArray())
                .ToArray();
        }

        private void SetLookupDocumentIds(IEnumerable<Guid> synchronizationDocumentIds)
        {
            var storageProperties = typeof(LookupDocument)
                .GetProperties()
                .Where(property => property.Name.StartsWith("LookupDocumentIds_"))
                .ToArray();
            
            bool success = synchronizationDocumentIds
                .Split(index => 4096)
                .SelectReduce(storageProperties,
                    (synchronizationDocumentIdSet, propertyInfosAvailable, next, skip) =>
                    {
                        var propertyInfo = propertyInfosAvailable.First();
                        propertyInfo.SetValue(this, synchronizationDocumentIdSet.ToByteArrayOfGuids());
                        return next(true, propertyInfosAvailable.Skip(1).ToArray());
                    },
                    (bool[] operated, System.Reflection.PropertyInfo[] propertyInfosAvailable) =>
                    {
                        foreach (var propertyInfo in propertyInfosAvailable)
                        {
                            propertyInfo.SetValue(this, default(byte[]));
                        }
                        return operated.All();
                    });
        }

        public bool AddLookupDocumentId(Guid synchronizationDocumentId)
        {
            try
            {
                var synchronizationDocumentIds = GetLookupDocumentIds();
                if (synchronizationDocumentIds.Contains(synchronizationDocumentId))
                    return false;
                SetLookupDocumentIds(synchronizationDocumentIds
                    .Append(synchronizationDocumentId)
                    .Distinct());
                return true;
            }
            catch (Exception ex)
            {
                ex.GetType();
                throw;
            }
        }

        public bool RemoveLookupDocumentId(Guid lookupDocumentId)
        {
            var lookupDocumentIds = GetLookupDocumentIds();
            if (!lookupDocumentIds.Contains(lookupDocumentId))
                return false;
            SetLookupDocumentIds(lookupDocumentIds
                .Where(docId => docId != lookupDocumentId)
                .Distinct());
            return true;
        }
    }


}