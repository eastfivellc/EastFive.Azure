using EastFive.Analytics;
using EastFive.Api;
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
    public class MessageWriter
    {
        private Task task;

        public MessageWriter(ILoggerWithEvents logging, Guid eventId, CancellationToken cancellationToken)
        {
            ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
            AutoResetEvent autoMutex = new AutoResetEvent(false);

            OnMessageHandler onMessage = (message) =>
            {
                queue.Enqueue(message);
                autoMutex.Set();
            };

            logging.OnInformation += onMessage;
            logging.OnTrace += onMessage;
            logging.OnWarning += onMessage;
            if (cancellationToken.IsDefault())
                cancellationToken = new CancellationToken();

            task = Task.Run(
                async () =>
                {
                    var order = 0;
                    while (true)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            if (!autoMutex.WaitOne(TimeSpan.FromSeconds(10)))
                                continue;
                        }
                        if (!queue.TryDequeue(out string message))
                        {
                            if (cancellationToken.IsCancellationRequested)
                                return;
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
                },
                cancellationToken);
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
