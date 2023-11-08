using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Linq.Async;

namespace BlackBarLabs.Persistence.Azure.StorageTables
{
    public partial class AzureStorageRepository
    {

        public static TResult Connection<TResult>(Func<AzureStorageRepository, TResult> onConnected)
        {
            var repo = AzureStorageRepository.CreateRepository(EastFive.Azure.AppSettings.Persistence.StorageTables.ConnectionString);

            return onConnected(repo);
        }
    }
}
