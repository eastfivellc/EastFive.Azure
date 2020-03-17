using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using BlackBarLabs.Persistence.Azure;
using BlackBarLabs.Persistence.Azure.Attributes;
using BlackBarLabs.Persistence.Azure.StorageTables;
using BlackBarLabs.Web;
using EastFive.Web.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace EastFive.Api.Azure.Monitoring
{
    [Serializable]
    [DataContract]
    [StorageResource(typeof(TwoThousandEighteenYearMonthGenerator))]
    public class MonitoringDocument : TableEntity
    {
        public MonitoringDocument()
        {
        }

        public MonitoringDocument(string id)
        {
            RowKey = id;
            PartitionKey = RowKey.GeneratePartitionKey();
        }

        public Guid Id => Guid.Parse(this.RowKey);
        public Guid AuthenticationId { get; set; }
        public DateTime Time { get; set; }
        public string Method { get; set; }
        public string Controller { get; set; }
        public string Content { get; set; }

        private static AzureStorageRepository GetRepo(string storageAppSettingKey)
        {
            return storageAppSettingKey.ConfigurationString(
                storageSetting =>
                {
                    var cloudStorageAccount = CloudStorageAccount.Parse(storageSetting);
                    var repo = new AzureStorageRepository(cloudStorageAccount);
                    return repo;
                });
        }

        public static Task<TResult> CreateAsync<TResult>(Guid id, Guid authenticationId, DateTime time, string method, string controller, string content,
                AzureStorageRepository repo,
            Func<TResult> onSuccess)
        {
            var doc = new MonitoringDocument();
            doc.AuthenticationId = authenticationId;
            doc.Time = time;
            doc.Method = method;
            doc.Controller = controller;
            doc.Content = content;

            var monthBucketedPartitionKey = GenerateMonthBucketedPartitionKey(time);
            return repo.CreateAsync(id, monthBucketedPartitionKey, doc,
                () => onSuccess(),
                () => throw new Exception("Guid not unique"));
        }

        public async static Task<TResult> GetByMonthAsync<TResult>(DateTime month,
                AzureStorageRepository repo,
            Func<IEnumerable<MonitoringDocument>, TResult> onFound)
        {
            var monthBucketedPartitionKey = GenerateMonthBucketedPartitionKey(month);
            return onFound(await repo.FindAllByPartitionAsync<MonitoringDocument>(monthBucketedPartitionKey));
        }

        private static string GenerateMonthBucketedPartitionKey(DateTime date)
        {
            return date.ToString("yyyy") + date.ToString("MM");
        }
    }
}
