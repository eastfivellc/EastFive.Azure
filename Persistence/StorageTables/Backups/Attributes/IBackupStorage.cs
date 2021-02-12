using System;
using System.Collections.Generic;
using System.Reflection;

namespace EastFive.Azure.Persistence.StorageTables.Backups
{
    public interface IBackupStorageType
    {
        IEnumerable<StorageResourceInfo> GetStorageResourceInfos(Type t);
    }

    public interface IBackupStorageMember
    {
        IEnumerable<StorageResourceInfo> GetStorageResourceInfos(MemberInfo t);
    }
}
