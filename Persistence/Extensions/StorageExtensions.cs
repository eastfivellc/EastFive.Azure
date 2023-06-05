using System;
using EastFive.Extensions;
using EastFive.Persistence;

namespace EastFive.Azure.Persistence
{
	public static class StorageExtensions
	{
        public static T CopyStoragePropertiesTo<T>(this T obj, T objectToUpdate)
        {
            objectToUpdate = obj
                .CloneObjectPropertiesWithAttributeInterface(
                    objectToUpdate, typeof(IPersistInAzureStorageTables), inherit:true);
            return objectToUpdate;
        }
	}
}

