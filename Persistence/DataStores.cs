using System;
using BlackBarLabs.Persistence.Azure.StorageTables;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;

using System.Linq;
using System.Threading.Tasks;
using BlackBarLabs.Web;
using EastFive.Web.Configuration;

namespace BlackBarLabs.Persistence.Azure
{
    public class DataStores
    {
        private readonly string azureKey;
        private readonly string documentDbEndpointUri;
        private readonly string documentDbPrimaryKey;
        private readonly string documentDbDatabaseName;

        private CloudStorageAccount cloudStorageAccount;

        // Contexts
        private CloudBlobClient blobClient;
        private AzureStorageRepository azureStorageRepository;

        public DataStores(string azureKey, string documentDbEndpointUri = null, string documentDbPrimaryKey = null, string documentDbDatabaseName = null)
        {
            this.azureKey = azureKey;
            this.documentDbEndpointUri = documentDbEndpointUri;
            this.documentDbPrimaryKey = documentDbPrimaryKey;
            this.documentDbDatabaseName = documentDbDatabaseName;

            cloudStorageAccount = this.azureKey.ConfigurationString(
                storageSetting => CloudStorageAccount.Parse(storageSetting));
        }

        private static readonly object AstLock = new object();
        public AzureStorageRepository AzureStorageRepository
        {
            get
            {
                if (azureStorageRepository != null)
                    return azureStorageRepository;

                lock (AstLock)
                    if (azureStorageRepository == null)
                    {
                        cloudStorageAccount = azureKey.ConfigurationString(
                            storageSetting => CloudStorageAccount.Parse(storageSetting));
                        
                        azureStorageRepository = new AzureStorageRepository(cloudStorageAccount);
                    }

                return azureStorageRepository;
            }
            private set { azureStorageRepository = value; }
        }

        private static readonly object BlobStoreLock = new object();
        public CloudBlobClient BlobStore
        {
            get
            {
                if (blobClient != null) return blobClient;

                lock (BlobStoreLock)
                    if (blobClient == null)
                    {
                        if (cloudStorageAccount == null)
                        {
                            cloudStorageAccount = azureKey.ConfigurationString(
                                storageSetting => CloudStorageAccount.Parse(storageSetting));
                        }
                        blobClient = cloudStorageAccount.CreateCloudBlobClient();
                        blobClient.DefaultRequestOptions.RetryPolicy = new ExponentialRetry(TimeSpan.FromSeconds(1), 10);
                        blobClient.GetContainerReference("media")
                            .CreateIfNotExistsAsync(BlobContainerPublicAccessType.Container,
                                new BlobRequestOptions
                                {

                                },
                                new OperationContext
                                { });

                    }

                return blobClient;
            }
        }
    }
}

