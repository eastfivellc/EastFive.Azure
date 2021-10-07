using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;
using System.Threading;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Collections.Generic;
using System.Linq;

namespace EastFive.Azure.Persistence.StorageTables.Driver
{
    public class E5CloudTable
    {
        private CloudTable cloudTable;

        public E5CloudTable(CloudTable cloudTable)
        {
            this.cloudTable = cloudTable;
        }

        private static AutoResetEvent operationsLock = new AutoResetEvent(true);
        private static System.Threading.AutoResetEvent[] operations = new AutoResetEvent[]
            {
                new AutoResetEvent(true),
                new AutoResetEvent(true),
                new AutoResetEvent(true),
                new AutoResetEvent(true),
                new AutoResetEvent(true),
                new AutoResetEvent(true),
                new AutoResetEvent(true),
                new AutoResetEvent(true),
                new AutoResetEvent(true),
                new AutoResetEvent(true),
                new AutoResetEvent(true),
            };

        public async Task<TableResult> ExecuteAsync(TableOperation tableOperation)
        {
            var operation = GetOperation();
            var result = await cloudTable.ExecuteAsync(tableOperation);
            operation.Set();
            return result;

            AutoResetEvent GetOperation()
            {
                var operationsSet = default(AutoResetEvent[]);
                if (operationsLock.WaitOne(TimeSpan.FromSeconds(1.0)))
                {
                    try
                    {
                        var properties = IPGlobalProperties.GetIPGlobalProperties();
                        var httpConnections = properties.GetActiveTcpConnections();
                        if (httpConnections.Length > 200)
                        {
                            if (operations.Length > 20)
                            {
                                operations = operations
                                    .Take(operations.Length - 1)
                                    .ToArray();
                            }
                        }
                        operationsSet = operations.AsCopy();
                    }
                    finally
                    {
                        operationsLock.Set();
                    }
                }

                do
                {
                    var waitIndex = Mutex.WaitAny(operationsSet, TimeSpan.FromMilliseconds(100));
                    if (WaitHandle.WaitTimeout != waitIndex)
                    {
                        var waitMutex = operationsSet[waitIndex];
                        if (waitMutex.WaitOne(TimeSpan.FromMilliseconds(10)))
                            return waitMutex;

                        continue;
                    }

                    try
                    {
                        var properties = IPGlobalProperties.GetIPGlobalProperties();
                        var httpConnections = properties.GetActiveTcpConnections();
                        if (httpConnections.Length < 150)
                        {
                            var waitEventToUse = new AutoResetEvent(true);
                            operations = operations
                                .Append(waitEventToUse)
                                .ToArray();
                            return waitEventToUse;
                        }
                        operationsSet = operations.AsCopy();
                    }
                    finally
                    {
                        operationsLock.Set();
                    }
                } while (true);
            }

        }
    }
}

