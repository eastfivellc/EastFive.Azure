using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EastFive;
using EastFive.Api;
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

        public AzureBlobFileSystemUri()
        {

        }

        public AzureBlobFileSystemUri(string containerName, string path)
        {
            this.containerName = containerName;
            this.path = path;
        }

        public string AbfssUri
		{
			get
			{
				return EastFive.Azure.AppSettings.Persistence.DataLake.ConnectionString.ConfigurationString(
					dlConnectionString =>
					{
						var accountNameRegexVariable = "accountName";
						if (!dlConnectionString.TryMatchRegex($"AccountName=(?<{accountNameRegexVariable}>[^;]+)", out (string, string)[] matches))
							return string.Empty;

						return matches
							.Where(match => accountNameRegexVariable.Equals(match.Item1, StringComparison.OrdinalIgnoreCase))
							.First(
								(match, next) =>
                                {
                                    var storageName = match.Item2;
                                    return $"abfss://{containerName}@{storageName}.dfs.core.windows.net/{path}";
                                },
								() => string.Empty);
					},
					(why) => string.Empty);
            }
			set
			{
                var containerNameRegexVariable = "containerName";
                var storageNameRegexVariable = "storageName";
                var pathRegexVariable = "path";
                var fileNameRegexVariable = "filename";
				if (!value.TryMatchRegex(
						$"abfss://(?<{containerNameRegexVariable}>[^@]+)@(?<{storageNameRegexVariable}>[^\\.]+).dfs.core.windows.net/(?<{pathRegexVariable}>[^@]+/)(?<{fileNameRegexVariable}>[^/]+)",
						out (string, string)[] matches))
					return;

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
            }
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

