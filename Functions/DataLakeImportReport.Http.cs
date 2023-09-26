using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive;
using EastFive.Linq;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Extensions;
using EastFive.Api.Auth;
using EastFive.Azure.Login;
using EastFive.Azure.Auth;
using EastFive.Reflection;
using System.Reflection;

namespace EastFive.Azure.Functions
{
    [FunctionViewController(
        Route = nameof(DataLakeImportReport),
        ContentType = "application/x-"+ nameof(DataLakeImportReport) + "+json")]
    public partial struct DataLakeImportReport
    {
        public struct Audit
        {
            public DateTime? lastProcessed;
            public string payer;
            public string profile;
            public bool payerMatch;
            public string gapMappings;
            public string[] measuresTracked;
            public string[] columnsIgnored;
            public string[] columnsExported;
            public int? rowsTotal;
            public int? patientsTotal;
            public int? patientsUnique;
            public int? patientsMatched;
            public int? patientsMatchedRecon;
            public int? patientsMatchedRoster;
            public int? patientsMatchedAffirm;
            public bool? success;
        }

        [HttpAction("Audit")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> AuditAsync(
                [QueryId] Guid exportId,
                [QueryParameter] DateTime start,
                [QueryParameter] int days,
                [QueryParameter] string exportTypeName,
            ContentTypeResponse<string[]> onFound,
            BadRequestResponse onBadType,
            NotFoundResponse onNotFound)
        {
            return await typeof(IExportFromDatalake)
                .GetAllSubClassTypes(a => true)
                .Where(
                    type => String.Equals(type.Name, exportTypeName, StringComparison.OrdinalIgnoreCase))
                .First(
                    async (exportType, next) =>
                    {
                        return await await exportId.StorageGetAsync(exportType,
                            async exportObj =>
                            {
                                var export = (IExportFromDatalake)exportObj;
                                var files = await export.GetDatalakeFiles().ToArrayAsync();
                                var dir = export.exportFolder;
                                var reports = await Enumerable
                                    .Range(0, days)
                                    .SelectAsyncMany(
                                        dayOffset =>
                                        {
                                            var when = start + TimeSpan.FromDays(dayOffset);
                                            return GetFromStorage(exportId, when);
                                        })
                                    .ToArrayAsync();

                                var unprocessedfiles = files
                                    .Select(
                                        file =>
                                        {
                                            var matchingReports = reports
                                                .Where(
                                                    report =>
                                                    {
                                                        return String.Equals(report.path, file.Name, StringComparison.Ordinal);
                                                    })
                                                .ToArray();
                                            return (file, matchingReports);
                                        })
                                    .Where(
                                        kvp =>
                                        {
                                            var (file, matchingReports) = kvp;
                                            var didComplete = DataLakeImportReport.DidComplete(matchingReports,
                                                out var maxLInesProcessed, out var missingLines, out var erroredLines);
                                            return !didComplete;
                                        })
                                    .Select(
                                        kvp =>
                                        {
                                            return kvp.file.Name;
                                        })
                                    .ToArray();

                                return onFound(unprocessedfiles);
                            },
                            () =>
                            {
                                return onNotFound().AsTask();
                            });
                    },
                    () =>
                    {
                        return onBadType().AsTask();
                    });

            
        }
    }
}

