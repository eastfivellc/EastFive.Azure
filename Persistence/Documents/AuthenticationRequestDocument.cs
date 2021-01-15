using System;
using System.Runtime.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

using BlackBarLabs.Persistence.Azure.Attributes;
using BlackBarLabs.Persistence.Azure.StorageTables;
using EastFive.Serialization;
using EastFive.Azure;

namespace EastFive.Security.SessionServer.Persistence.Documents
{
    [StorageResourceNoOp]
    public class AuthenticationRequestDocument : TableEntity
    {
        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id
        {
            get { return Guid.Parse(this.RowKey); }
        }

        #region Properties

        public string Method { get; set; }
        public string Name { get; set; }
        public string Token { get; set; }

        public string Action { get; set; }
        public Guid? LinkedAuthenticationId { get; set; }
        public string RedirectUrl { get; set; }
        public string RedirectLogoutUrl { get; set; }
        public DateTime? Deleted { get; set; }

        public byte[] ExtraParams { get; set; }

        internal IDictionary<string, string> GetExtraParams()
        {
            return ExtraParams.FromByteArray(
                (keyBytes) => System.Text.Encoding.UTF8.GetString(keyBytes),
                (valueBytes) => System.Text.Encoding.UTF8.GetString(valueBytes));
        }

        internal void SetExtraParams(IDictionary<string, string> extraParams)
        {
            ExtraParams = extraParams.ToByteArray(
                (key) => System.Text.Encoding.UTF8.GetBytes(key),
                (value) => System.Text.Encoding.UTF8.GetBytes(value));
        }

        #endregion

        public static Task<TResult> UpdateAsync<TResult>(Guid integrationId,
            Func<Integration, Func<Integration, Task<string>>, Task<TResult>> onFound,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                ast => ast.UpdateAsync<AuthenticationRequestDocument, TResult>(integrationId,
                    (doc, updateAsync) =>
                    {
                        var integration = Convert(doc);
                        return onFound(integration,
                            async (updatedIntegration) =>
                            {
                                doc.SetExtraParams(updatedIntegration.parameters);
                                await updateAsync(doc);
                                return doc.RedirectUrl;
                            });
                    },
                    onNotFound));
        }

        public static Integration Convert(AuthenticationRequestDocument doc)
        {
            return new EastFive.Azure.Integration
            {
                integrationId = doc.Id,
                method = doc.Method,
                parameters = doc.GetExtraParams(),
                authorizationId = doc.LinkedAuthenticationId.GetValueOrDefault(),
            };
        }
    }
}
