using System;
using System.Linq;

using EastFive;
using EastFive.Linq;
using EastFive.Persistence;
using EastFive.Text;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Persistence.StorageTables
{
	public class AzureBlobFileSystemUri
	{
		public string containerName;
		public string path;
		public string fileName;

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
                                    return $"abfss://{containerName}@{storageName}.dfs.core.windows.net/{path}/{fileName}";
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
						$"abfss://(?<{containerNameRegexVariable}>[^@]+)@(?<{storageNameRegexVariable}>[^\\.]+).dfs.core.windows.net(?<{pathRegexVariable}>[^@]+/)(?<{fileNameRegexVariable}>[^/]+)",
						out (string, string)[] matches))
					return;

                this.containerName = matches
                    .Where(match => containerNameRegexVariable.Equals(match.Item1, StringComparison.OrdinalIgnoreCase))
                    .First(
                        (match, next) => match.Item2,
                        () => string.Empty);
                this.path = matches
                    .Where(match => pathRegexVariable.Equals(match.Item1, StringComparison.OrdinalIgnoreCase))
                    .First(
                        (match, next) => match.Item2,
                        () => string.Empty);
                this.fileName = matches
                    .Where(match => fileNameRegexVariable.Equals(match.Item1, StringComparison.OrdinalIgnoreCase))
                    .First(
                        (match, next) => match.Item2,
                        () => string.Empty);
            }
		}
	}
}

