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
		//public async Task<bool> PurgeAsync()
  //      {
  //          var deletedTableName = await this.TableClient.GetTables()
  //              .Select(
  //                  async table =>
  //                  {
  //                      await table.DeleteAsync();
  //                      return table.Name;
  //                  })
  //              .Await(readAhead:50)
  //              .ToArrayAsync();
  //          return true;
  //      }

        public static TResult Connection<TResult>(Func<AzureStorageRepository, TResult> onConnected)
        {
            var repo = AzureStorageRepository.CreateRepository(EastFive.Azure.AppSettings.ASTConnectionStringKey);

            return onConnected(repo);
        }

        //public static Task<TResult> Transaction<TResult>(Func<RollbackAsync<TResult>, AzureStorageRepository,  Func<TResult>> onConnected)
        //{
        //    return AzureStorageRepository.Connection(
        //        connection =>
        //        {
        //            var rollback = new RollbackAsync<TResult>();
        //            var onSuccess = onConnected(rollback, connection);
        //            return rollback.ExecuteAsync(onSuccess);
        //        });
        //}
    }
}
