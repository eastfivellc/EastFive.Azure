using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EastFive;
using EastFive.Api;
using EastFive.Configuration;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Persistence;
using EastFive.Text;
using EastFive.Web.Configuration;

using Microsoft.Azure.Cosmos.Table;
using Newtonsoft.Json;

namespace EastFive.Azure.Persistence.StorageTables
{
    [AzureBlobFileSystemUriStorage]
    public class AzureBlobFileSystemUri
    {
        public string containerName;
        public string path;
        public string storageName;

        public AzureBlobFileSystemUri()
        {

        }

        public AzureBlobFileSystemUri(string containerName, string path, string storageName)
        {
            this.containerName = containerName;
            this.path = path;
            this.storageName = storageName;
        }

        public static AzureBlobFileSystemUri FromConnectionString(ConnectionString connectionStringKey,
            string containerName, string path)
        {
            var storageName = connectionStringKey.ConfigurationString(ParseConnectionStringForAccountName);
            return new AzureBlobFileSystemUri(containerName, path, storageName);
        }

        public AzureBlobFileSystemUri AppendToPath(string fileNameOrDirectory)
        {
            var newPath = this.path.EndsWith('/') ?
                $"{this.path}{fileNameOrDirectory}"
                :
                $"{this.path}/{fileNameOrDirectory}";
            return new AzureBlobFileSystemUri(this.containerName, newPath, this.storageName);
        }

        private static string ParseConnectionStringForAccountName(string connectionString)
        {
            var accountNameRegexVariable = "accountName";
            if (!connectionString.TryMatchRegex($"AccountName=(?<{accountNameRegexVariable}>[^;]+)", out (string, string)[] matches))
                return string.Empty;

            return matches
                .Where(match => accountNameRegexVariable.Equals(match.Item1, StringComparison.OrdinalIgnoreCase))
                .First(
                    (match, next) =>
                    {
                        var storageName = match.Item2;
                        return storageName;
                    },
                    () => string.Empty);
        }

        public ConnectionString ConnectionString
        {
            get
            {
                var connectionString = EastFive.Azure.AppSettings.Persistence.DataLake.ConnectionString.ConfigurationString(x => x);
                var dlAccoutnName = ParseConnectionStringForAccountName(connectionString);
                if (String.Equals(dlAccoutnName, this.storageName))
                    return EastFive.Azure.AppSettings.Persistence.DataLake.ConnectionString;

                return EastFive.Azure.AppSettings.Persistence.StorageTables.ConnectionString;
            }
        }

        public string AbfssUri
        {
            get
            {
                return $"abfss://{containerName}@{storageName}.dfs.core.windows.net/{path}";
            }
            set
            {
                var containerNameRegexVariable = "containerName";
                var storageNameRegexVariable = "storageName";
                var pathRegexVariable = "path";
                var fileNameRegexVariable = "filename";
                if (value.TryMatchRegex(
                        $"abfss://(?<{containerNameRegexVariable}>[^@]+)@(?<{storageNameRegexVariable}>[^\\.]+).dfs.core.windows.net/(?<{pathRegexVariable}>[^@]+/)(?<{fileNameRegexVariable}>[^/]+)",
                        out (string, string)[] matches))
                {
                    this.storageName = matches
                        .Where(match => storageNameRegexVariable.Equals(match.Item1, StringComparison.OrdinalIgnoreCase))
                        .First(
                            (match, next) => match.Item2,
                            () => string.Empty);
                    this.containerName = matches
                        .Where(match => containerNameRegexVariable.Equals(match.Item1, StringComparison.OrdinalIgnoreCase))
                        .First(
                            (match, next) => match.Item2,
                            () => string.Empty);
                    var path = matches
                        .Where(match => pathRegexVariable.Equals(match.Item1, StringComparison.OrdinalIgnoreCase))
                        .First(
                            (match, next) => match.Item2,
                            () => string.Empty);
                    var fileName = matches
                        .Where(match => fileNameRegexVariable.Equals(match.Item1, StringComparison.OrdinalIgnoreCase))
                        .First(
                            (match, next) => match.Item2,
                            () => string.Empty);

                    this.path = fileName.HasBlackSpace() ?
                        $"{path}{fileName}"
                        :
                        path;
                    return;
                }


                if (value.TryMatchRegex(
                    $"abfss://(?<{containerNameRegexVariable}>[^@]+)@(?<{storageNameRegexVariable}>[^\\.]+).dfs.core.windows.net/(?<{pathRegexVariable}>.+)",
                    out (string, string)[] matchesNoFilename))
                {
                    this.containerName = matchesNoFilename
                        .Where(match => containerNameRegexVariable.Equals(match.Item1, StringComparison.OrdinalIgnoreCase))
                        .First(
                            (match, next) => match.Item2,
                            () => string.Empty);
                    this.path = matchesNoFilename
                        .Where(match => pathRegexVariable.Equals(match.Item1, StringComparison.OrdinalIgnoreCase))
                        .First(
                            (match, next) => match.Item2,
                            () => string.Empty);
                    this.storageName = matchesNoFilename
                        .Where(match => storageNameRegexVariable.Equals(match.Item1, StringComparison.OrdinalIgnoreCase))
                        .First(
                            (match, next) => match.Item2,
                            () => string.Empty);
                    return;
                }

                this.path = value;
            }
        }

        public bool IsValid()
        {
            return this.containerName.HasBlackSpace() && this.storageName.HasBlackSpace();
        }
    }

    public class AzureBlobFileSystemUriStorageAttribute : System.Attribute, ICastEntityProperty, IBindEntityProperty, ICastJson
    {

        public TResult BindEntityProperty<TResult>(EntityProperty value, Type type,
            Func<object, TResult> onBound,
            Func<TResult> onFailedToBind)
        {
            var abfsUriValue = new AzureBlobFileSystemUri();
            if (value.PropertyType != EdmType.String)
                return onBound(abfsUriValue);

            if (value.StringValue.IsNullOrWhiteSpace())
                return onBound(abfsUriValue);

            abfsUriValue.AbfssUri = value.StringValue;
            return onBound(abfsUriValue);
        }

        public TResult CastEntityProperty<TResult>(object value, Type valueType,
            Func<EntityProperty, TResult> onValue,
            Func<TResult> onNoCast)
        {
            if (value.IsDefaultOrNull())
                return onValue(new EntityProperty(default(string)));

            var abfsUriValue = (AzureBlobFileSystemUri)value;
            return onValue(new EntityProperty(abfsUriValue.AbfssUri));
        }

        public bool CanConvert(Type type, object value, IHttpRequest httpRequest, IApplication application)
        {
            return type.IsAssignableTo(typeof(AzureBlobFileSystemUri));
        }

        public async Task WriteAsync(JsonWriter writer, JsonSerializer serializer, Type type, object value, IHttpRequest httpRequest, IApplication application)
        {
            var abfsUriValue = (AzureBlobFileSystemUri)value;

            await writer.WriteValueAsync(abfsUriValue.AbfssUri);
        }
    }
}

