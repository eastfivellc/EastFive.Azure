using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;
using System.Threading;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Collections.Generic;
using EastFive.Web.Configuration;
using System.Linq;

namespace EastFive.Persistence.Azure.StorageTables.Driver
{
    public class E5CloudTable
    {
        public CloudTable cloudTable;

        /// <summary>
        /// When the total of HTTP connections reaches this point the connection pool
        /// will _begin_ being reduced. This should be the larger than
        /// <para>ConnectionCountGrowthStoppingPoint</para>.
        /// </summary>
        /// /// <remarks>
        /// The total HTTP connections can grow outside of this pool as this is the total
        /// connections count for the machine.
        /// </remarks>
        public static int ConnectionCountReductionPoint = 200;

        /// <summary>
        /// When the total of HTTP connections reaches this point the connection pool
        /// will stop growing. This should be the smaller than
        /// <para>ConnectionCountReductionPoint</para>.
        /// </summary>
        /// <remarks>
        /// The total HTTP connections can grow outside of this pool as this is the total
        /// connections count for the machine.
        /// </remarks>
        public static int ConnectionCountGrowthStoppingPoint = 150;

        /// <summary>
        /// Size of the connection pool will not go below this point.
        /// </summary>
        public static int MinimumParallelConnections = 20;

        public static void LoadConfigurations()
        {
            ConnectionCountReductionPoint = EastFive.Azure.AppSettings.Persistence.StorageTables.ConnectionCountReductionPoint
                .ConfigurationLong(
                    (connectionCountReductionPoint) => (int)connectionCountReductionPoint,
                    (why) => ConnectionCountReductionPoint);
            ConnectionCountGrowthStoppingPoint = EastFive.Azure.AppSettings.Persistence.StorageTables.ConnectionCountGrowthStoppingPoint
                .ConfigurationLong(
                    (connectionCountGrowthStoppingPoint) => (int)connectionCountGrowthStoppingPoint,
                    (why) => ConnectionCountGrowthStoppingPoint);
            MinimumParallelConnections = EastFive.Azure.AppSettings.Persistence.StorageTables.MinimumParallelConnections
                .ConfigurationLong(
                    (minimumParallelConnections) => (int)minimumParallelConnections,
                    (why) => MinimumParallelConnections);
        }

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
            try
            {
                System.Diagnostics.Debug.WriteLine($"START: {tableOperation.Entity.GetHashCode()}");
                var result = await cloudTable.ExecuteAsync(tableOperation);
                System.Diagnostics.Debug.WriteLine($"END--: {tableOperation.Entity.GetHashCode()}");
                return result;
            } finally
            {
                operation.Set();
            }

            AutoResetEvent GetOperation()
            {
                var operationsSet = AccessOperationsSet();
                do
                {
                    var waitIndex = Mutex.WaitAny(operationsSet, TimeSpan.FromMilliseconds(1));
                    if (WaitHandle.WaitTimeout != waitIndex)
                    {
                        var waitMutex = operationsSet[waitIndex];
                        return waitMutex;
                    }

                    try
                    {
                        var properties = IPGlobalProperties.GetIPGlobalProperties();
                        var httpConnections = properties.GetActiveTcpConnections();
                        var connectionCount = httpConnections.Length;
                        if (connectionCount < ConnectionCountGrowthStoppingPoint)
                        {
                            var waitEventToUse = new AutoResetEvent(true);
                            if (operationsLock.WaitOne(TimeSpan.FromSeconds(0.1)))
                            {
                                try
                                {
                                    operations = operations
                                        .Append(waitEventToUse)
                                        .ToArray();
                                    System.Diagnostics.Debug.WriteLine($"BUMP {operations.Length-1} => {operations.Length} [{connectionCount}]");
                                    operationsSet = operations.AsCopy();
                                    return waitEventToUse;
                                }
                                finally
                                {
                                    operationsLock.Set();
                                }
                            }
                        }
                    } catch(Exception)
                    {

                    }
                } while (true);


                AutoResetEvent[] AccessOperationsSet()
                {
                    while (true)
                    {
                        if (operationsLock.WaitOne(TimeSpan.FromSeconds(1.0)))
                        {
                            try
                            {
                                var properties = IPGlobalProperties.GetIPGlobalProperties();
                                var httpConnections = properties.GetActiveTcpConnections();
                                var connectionCount = httpConnections.Length;
                                if (connectionCount > ConnectionCountReductionPoint)
                                {
                                    if (operations.Length > MinimumParallelConnections)
                                    {
                                        operations = operations
                                            .Take(operations.Length - 1)
                                            .ToArray();
                                        System.Diagnostics.Debug.WriteLine($"DROP {operations.Length + 1} => {operations.Length} [{connectionCount}]");
                                    }
                                }
                                return operations.AsCopy();
                            }
                            finally
                            {
                                operationsLock.Set();
                            }
                        }
                    }
                }
            }
        }
    }
}

