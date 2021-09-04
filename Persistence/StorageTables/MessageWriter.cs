using EastFive.Analytics;
using EastFive.Api;
using EastFive.Azure.Functions;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EastFive.Azure.Persistence.StorageTables
{
    public class MessageWriter : IDisposable
    {
        private static readonly TimeSpan mutexWait = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan countdownWait = mutexWait + TimeSpan.FromSeconds(5);

        private readonly ILoggerWithEvents logging;
        private readonly LinkedTokenSource linkedToken;
        private readonly ConcurrentQueue<string> queue;
        private readonly AutoResetEvent autoMutex;
        private readonly CountdownEvent countdown;
        private Task task;
        private bool disposed;

        private void onMessage(string message)
        {
            this.queue.Enqueue(message);
            this.autoMutex.Set();
        }

        public MessageWriter(ILoggerWithEvents logging, Guid eventId, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsDefault())
                cancellationToken = new CancellationToken();

            this.logging = logging;
            linkedToken = new LinkedTokenSource(cancellationToken,
                (cancelledByAzure, cancelledManually) =>
                {
                    return false.AsTask();
                });

            queue = new ConcurrentQueue<string>();
            autoMutex = new AutoResetEvent(false);
            countdown = new CountdownEvent(0);
            this.logging.OnInformation += onMessage;
            this.logging.OnTrace += onMessage;
            this.logging.OnWarning += onMessage;

            task = Task.Run(
                async () =>
                {
                    try
                    {
                        countdown.Reset(1);
                        var order = 0;
                        while (true)
                        {
                            if (!linkedToken.Token.IsCancellationRequested)
                            {
                                if (queue.IsEmpty && !autoMutex.WaitOne(mutexWait))
                                    continue;
                            }
                            if (!queue.TryDequeue(out string message))
                            {
                                if (linkedToken.Token.IsCancellationRequested)
                                    break;
                                continue;
                            }

                            var messageLine = new EventMessageLine
                            {
                                eventMessageLineRef = Ref<EventMessageLine>.NewRef(),
                                eventId = eventId,
                                order = order,
                                when = DateTime.UtcNow,
                                message = message,
                            };
                            order++;
                            bool created = await messageLine.StorageCreateAsync(
                                discard => true);
                        }
                    }
                    finally
                    {
                        countdown.Signal();
                    }
                },
                linkedToken.Token);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                this.logging.OnInformation -= onMessage;
                this.logging.OnTrace -= onMessage;
                this.logging.OnWarning -= onMessage;
                linkedToken.Source.Cancel();    // cancels loop
                autoMutex.Set();                // causes WaitOne() to exit
                countdown.Wait();               // wait for Task to finish all queued messages and exit
                countdown.Dispose();
                autoMutex.Dispose();
                linkedToken.Dispose();
            }
            disposed = true;
        }
    }

    [StorageTable]
    public struct EventMessageLine : IReferenceable
    {
        [JsonIgnore]
        public Guid id => this.eventMessageLineRef.id;

        public const string IdPropertyName = "id";
        [JsonProperty(PropertyName = IdPropertyName)]
        [ApiProperty(PropertyName = IdPropertyName)]
        [RowKey]
        public IRef<EventMessageLine> eventMessageLineRef;

        public const string EventPropertyName = "event";
        [JsonProperty(PropertyName = EventPropertyName)]
        [ApiProperty(PropertyName = EventPropertyName)]
        [PartitionById]
        public Guid eventId;

        public const string MessagePropertyName = "message";
        [JsonProperty(PropertyName = MessagePropertyName)]
        [ApiProperty(PropertyName = MessagePropertyName)]
        [Storage]
        public string message;

        public const string WhenPropertyName = "when";
        [JsonProperty(PropertyName = WhenPropertyName)]
        [ApiProperty(PropertyName = WhenPropertyName)]
        [Storage]
        public DateTime when;

        public const string OrderPropertyName = "order";
        [JsonProperty(PropertyName = OrderPropertyName)]
        [ApiProperty(PropertyName = OrderPropertyName)]
        [Storage]
        public int order;
    }
}
