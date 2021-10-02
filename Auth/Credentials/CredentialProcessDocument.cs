using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Azure.Cosmos.Table;

using EastFive.Serialization;
using EastFive.Collections.Generic;
using EastFive.Linq;
using EastFive.Extensions;
using BlackBarLabs.Persistence.Azure;
using BlackBarLabs.Persistence.Azure.StorageTables;

namespace EastFive.Api.Azure.Credentials
{
    [Serializable]
    [DataContract]
    public class CredentialProcessDocument : TableEntity
    {
        public CredentialProcessDocument()
        {
        }

        public CredentialProcessDocument(string id)
        {
            RowKey = id;
            PartitionKey = RowKey.GeneratePartitionKey();
        }

        public Guid Id => Guid.Parse(this.RowKey);

        public Guid SessionId { get; set; }

        public Guid AuthorizationId { get; set; }
        
        public long Time { get; set; }

        public string Message { get; set; }


        public string Method { get; set; }

        public string Token { get; set; }

        public string RefreshToken { get; set; }

        public string Action { get; set; }

        public string Provider { get; set; }

        public string RedirectUrl { get; set; }

        public byte[] ValuesRedirect { get; set; }

        public IDictionary<string, string> GetValuesRedirect()
        {
            return ValuesRedirect.FromByteArray(
                (keyBytes) => System.Text.Encoding.UTF8.GetString(keyBytes),
                (valueBytes) => System.Text.Encoding.UTF8.GetString(valueBytes));
        }

        internal void SetValuesRedirect(IDictionary<string, string> extraParams)
        {
            ValuesRedirect = extraParams.ToByteArray(
                (key) => System.Text.Encoding.UTF8.GetBytes(key),
                (value) => System.Text.Encoding.UTF8.GetBytes(value));
        }

        public byte[] ValuesCredentialKeys { get; set; }
        public byte[] ValuesCredentialValues { get; set; }

        public IDictionary<string, string> GetValuesCredential()
        {
            var keys = ValuesCredentialKeys.ToStringsFromUTF8ByteArray();
            var values = ValuesCredentialValues.ToStringsFromUTF8ByteArray();
            return keys
                .Zip(values,
                    (k, v) => k.PairWithValue(v))
                .ToDictionary();
        }

        internal void SetValuesCredential(IDictionary<string, string> extraParams)
        {
            ValuesCredentialKeys = extraParams.NullToEmpty().SelectKeys().ToUTF8ByteArrayOfStrings();
            ValuesCredentialValues = extraParams.NullToEmpty().SelectValues().ToUTF8ByteArrayOfStrings();
        }
        
        internal static Task CreateAsync(Guid id,
            CredentialProcessDocument doc, AzureStorageRepository azureStorageRepository)
        {
            return azureStorageRepository.CreateAsync(id, doc,
                () => true,
                () => false);
        }

        public static Task<TResult> FindByIdAsync<TResult>(Guid id,
            Func<CredentialProcessDocument, TResult> onFound,
            Func<TResult> onNotFound,
            AzureStorageRepository azureStorageRepository)
        {
            return azureStorageRepository.FindByIdAsync(id,
                onFound,
                onNotFound);
        }

        public static Task<TResult> FindAllAsync<TResult>(
            Func<CredentialProcessDocument[], TResult> onFound,
            AzureStorageRepository azureStorageRepository)
        {
            return azureStorageRepository.FindAllAsync<CredentialProcessDocument, TResult>(
                (docs) =>
                {
                    return onFound(docs);
                });
        }

        public static async Task<TResult> UpdateAsync<TResult>(Guid id,
            Func<CredentialProcessDocument, Func<CredentialProcessDocument, Task>, Task<TResult>> onSuccess,
            AzureStorageRepository repo)
        {
            return await repo.UpdateAsync<CredentialProcessDocument, TResult>(id,
                (doc, saveAsync) => onSuccess(doc, (docUpdate) => saveAsync(docUpdate)),
                () => throw new NotImplementedException());
        }
    }
}
