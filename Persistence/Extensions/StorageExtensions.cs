using System;
using EastFive.Extensions;
using EastFive.Persistence;

namespace EastFive.Azure.Persistence
{
	public static class StorageExtensions
	{
        public static T CopyStoragePropertiesTo<T>(this T obj, T objectToUpdate, bool skipNullAndDefault = false)
        {
            objectToUpdate = obj
                .CloneObjectPropertiesWithAttributeInterface(
                    objectToUpdate, typeof(IPersistInAzureStorageTables),
                    skipNullAndDefault, inherit:true);
            return objectToUpdate;
        }
	}
}

