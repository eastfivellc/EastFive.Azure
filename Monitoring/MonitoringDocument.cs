﻿using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using BlackBarLabs.Persistence.Azure;
using BlackBarLabs.Persistence.Azure.StorageTables;
using BlackBarLabs.Web;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace EastFive.Azure.Api.Monitoring
{
    [Serializable]
    [DataContract]
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
            var storageSetting = ConfigurationContext.Instance.AppSettings[storageAppSettingKey];
            var cloudStorageAccount = CloudStorageAccount.Parse(storageSetting);
            var repo = new AzureStorageRepository(cloudStorageAccount);
            return repo;
        }

        public static async Task<TResult> CreateAsync<TResult>(Guid id, Guid authenticationId, DateTime time, string method, string controller, string content,
                AzureStorageRepository repo,
            Func<TResult> onSuccess)
        {
            var monthBucketedPartitionKey = GenerateMonthBucketedPartitionKey(time);
            return await repo.CreateOrUpdateAsync<MonitoringDocument, TResult>(id, monthBucketedPartitionKey,
                async (created, doc, saveAsync) =>
                {
                    doc.AuthenticationId = authenticationId;
                    doc.Time = time;
                    doc.Method = method;
                    doc.Controller = controller;
                    doc.Content = content;
                    await saveAsync(doc);
                    return onSuccess();
                });
        }

        private static string GenerateMonthBucketedPartitionKey(DateTime date)
        {
            return date.ToString("yyyy") + date.ToString("MM");
        }
    }
}
