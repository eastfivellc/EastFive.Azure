using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Microsoft.Azure.Cosmos.Table;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;

namespace EastFive.Azure.Persistence.StorageTables
{
    public static class TableExtensions
    {
        public static IEnumerableAsync<CloudTable> GetTables(this CloudTableClient tableClient)
        {
            var ranOnce = false;
            var continuationToken = default(TableContinuationToken);

            return EnumerableAsync.YieldBatch<CloudTable>(
                async (yieldReturn, yieldBreak) =>
                {
                    if (continuationToken.IsDefaultOrNull())
                        if (ranOnce)
                            return yieldBreak;

                    ranOnce = true;
                    var segment = await tableClient.ListTablesSegmentedAsync(continuationToken);
                    continuationToken = segment.ContinuationToken;

                    if (!segment.Results.Any())
                        return yieldBreak;
                    return yieldReturn(segment.Results.ToArray());
                });
        }
    }
}
