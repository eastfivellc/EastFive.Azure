using System;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Collections.Generic;

namespace EastFive.Messaging
{
    public abstract class QueueProcessor<TMessageParam>
    {
        private const string MESSAGE_PROPERTY_KEY_MESSAGE_NAME = "MessageName";
        private static string[] subscriptions = new string[] { };

        private bool processing = false;
        protected QueueProcessor(string subscription)
        {
            subscription = subscription.ToLower();
            subscriptions = subscriptions.Append(subscription).ToArray();
            var xexecutionThread = Web.Configuration.Settings.GetString(
                    Azure.AppSettings.ServiceBusConnectionString,
                serviceBusConnectionString =>
                {
                    var receiveClient = new Microsoft.Azure.ServiceBus.QueueClient(serviceBusConnectionString, subscription);
                    // TODO: Create the topic if it does not exist already but swallow the errors if manage privilige is not available

                    Func<Microsoft.Azure.ServiceBus.Message, CancellationToken, Task> processMessagesAsync =
                        async (message, cancellationToken) =>
                        {
                            try
                            {
                                processing = true;
                                await ProcessAsync(message, receiveClient);
                            }
                            catch(Exception ex)
                            {
                                ex.Message.GetType();
                            }
                            finally
                            {
                                processing = false;
                            }
                        };

                    var messageHandlerOptions = new Microsoft.Azure.ServiceBus.MessageHandlerOptions(ExceptionReceivedHandler)
                    {
                        // Maximum number of concurrent calls to the callback ProcessMessagesAsync(), set to 1 for simplicity.
                        // Set it according to how many messages the application wants to process in parallel.
                        MaxConcurrentCalls = 1,

                        // Indicates whether the message pump should automatically complete the messages after returning from user callback.
                        // False below indicates the complete operation is handled by the user callback as in ProcessMessagesAsync().
                        AutoComplete = false,

                        MaxAutoRenewDuration = TimeSpan.FromHours(1),
                    };

                    // Register the function that processes messages.
                    receiveClient.RegisterMessageHandler(processMessagesAsync, messageHandlerOptions);
                    return string.Empty;
                },
                (why) => why);
        }
        

        private static string GetSubscription()
        {
            return typeof(TMessageParam).FullName.Replace('.', '_');
        }

        private static string GetTopic(Type topic)
        {
            return topic.FullName.Replace('.', '_');
        }

        public enum MessageProcessStatus
        {
            Complete, ReprocessLater, ReprocessImmediately, Broken, NoAction,
        }

        private static TMessageParam ParseMessageParams(IDictionary<string, object> messageParamsDictionary)
        {
            var type = typeof(TMessageParam);
            var constructorInfo = type.GetConstructor(new Type[] { });
            return messageParamsDictionary.Aggregate(
                (TMessageParam)constructorInfo.Invoke(new object[] { }),
                (messageParams, propertyKvp) =>
                {
                    var propertyName = propertyKvp.Key;
                    var propertyInfo = type.GetProperty(propertyName);
                    if (propertyInfo != null) // Ignore extra fields at get stuffed into properties like "MessageName"
                    {
                        var propertyValue = messageParamsDictionary[propertyName];
                        propertyInfo.SetValue(messageParams, propertyValue);
                    }
                    return messageParams;
                });
        }

        protected static void EncodeMessageParams(IDictionary<string, object> messageParamsDictionary, TMessageParam messageParams)
        {
            foreach (var propertyInfo in typeof(TMessageParam).GetProperties())
            {
                var propertyName = propertyInfo.Name;
                var propertyValue = propertyInfo.GetValue(messageParams);
                messageParamsDictionary[propertyName] = propertyValue;
            }
        }

        protected abstract Task<TResult> ProcessMessageAsync<TResult>(TMessageParam messageParams,
            Func<Task<TResult>> onProcessed,
            Func<Task<TResult>> onUnprocessed,
            Func<string, Task<TResult>> onBrokenMessage);

        static Task ExceptionReceivedHandler(Microsoft.Azure.ServiceBus.ExceptionReceivedEventArgs exceptionReceivedEventArgs)
        {
            Console.WriteLine($"Message handler encountered an exception {exceptionReceivedEventArgs.Exception}.");
            var context = exceptionReceivedEventArgs.ExceptionReceivedContext;
            Console.WriteLine("Exception context for troubleshooting:");
            Console.WriteLine($"- Endpoint: {context.Endpoint}");
            Console.WriteLine($"- Entity Path: {context.EntityPath}");
            Console.WriteLine($"- Executing Action: {context.Action}");
            return Task.CompletedTask;
        }

        private async Task ProcessAsync(Microsoft.Azure.ServiceBus.Message message, Microsoft.Azure.ServiceBus.QueueClient client)
        //private async Task Execute(BrokeredMessage receivedMessage)
        {
            try
            {
                // Process the message
                var messageParams = ParseMessageParams(message.UserProperties);
                var messageAction = await ProcessMessageAsync<MessageProcessStatus>(messageParams,
                    async () =>
                    {
                        //message.Complete();
                        try
                        {
                            await client.CompleteAsync(message.SystemProperties.LockToken);
                            return MessageProcessStatus.Complete;
                        } catch(Microsoft.Azure.ServiceBus.MessageLockLostException)
                        {
                            // TODO: Renue lock client.Loc message.Lock
                            return MessageProcessStatus.Complete;
                        }
                    },
                    async () =>
                    {
                        if (message.ScheduledEnqueueTimeUtc < DateTime.UtcNow)
                            return MessageProcessStatus.NoAction;
                        // TODO: Update resent count and send error if it gets too large
                        // Let message get resent
                        try
                        {
                            await client.ScheduleMessageAsync(message, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10));
                            return MessageProcessStatus.ReprocessImmediately;
                        } catch(Exception)
                        {
                            return MessageProcessStatus.Broken;
                        }
                    },
                    async (whyReturned) =>
                    {
                        await client.DeadLetterAsync(message.SystemProperties.LockToken, whyReturned);
                        return MessageProcessStatus.Broken;
                    });
            }
            catch (Exception ex)
            {
                //var telemetryData = (new System.Collections.Generic.Dictionary<string, string>
                //        {
                //               { "receivedMessage.MessageId", receivedMessage.MessageId },
                //               { "receivedMessage.SessionId", receivedMessage.SessionId },
                //               { "receivedMessage.ContentType", receivedMessage.ContentType },
                //               { "receivedMessage.SequenceNumber", receivedMessage.SequenceNumber.ToString() },
                //               { "exception.Message", ex.Message },
                //               { "exception.StackTrace", ex.StackTrace },
                //        })
                //.Concat(receivedMessage.Properties
                //    .Select(prop => $"receivedMessage.Properties[{prop.Key}]".PairWithValue(
                //        null == prop.Value ? string.Empty : prop.Value.ToString())))
                //.ToDictionary();
                //telemetry.TrackException(ex, telemetryData);
                await client.DeadLetterAsync(message.SystemProperties.LockToken, ex.Message);
            }
        }

        protected static Task<TResult> SendAsync<TQueueProcessor, TResult>(TMessageParam messageParams, string subscription,
            Func<TResult> onSent,
            Func<string, TResult> onFailure)
             where TQueueProcessor : QueueProcessor<TMessageParam>
        {
            return Web.Configuration.Settings.GetString(
                    EastFive.Azure.AppSettings.ServiceBusConnectionString,
                async serviceBusConnectionString =>
                {
                    var message = new Microsoft.Azure.ServiceBus.Message();
                    EncodeMessageParams(message.UserProperties, messageParams);
                    message.UserProperties[MESSAGE_PROPERTY_KEY_MESSAGE_NAME] = subscription;
                    var sendClient = new Microsoft.Azure.ServiceBus.QueueClient(serviceBusConnectionString, subscription);
                    try
                    {
                        await sendClient.SendAsync(message);
                        return onSent();
                    }
                    catch (Exception ex)
                    {
                        return onFailure(ex.Message);
                    }
                    finally
                    {
                        await sendClient.CloseAsync();
                    }
                },
                onFailure.AsAsyncFunc()); 

        }
        
        public int Flush()
        {
            return Web.Configuration.Settings.GetString(
                    EastFive.Azure.AppSettings.ServiceBusConnectionString,
                serviceBusConnectionString =>
                {
                    do
                    {
                        Thread.Sleep(1000);
                    } while (processing);

                    //var nsmgr = Microsoft.ServiceBus.NamespaceManager.CreateFromConnectionString(serviceBusConnectionString);
                    //var subscription = subscriptions.First();
                    //var connectionBuilder = new Microsoft.Azure.ServiceBus.ServiceBusConnectionStringBuilder(serviceBusConnectionString);
                    //var manager = new Microsoft.Azure.ServiceBus.SubscriptionClient(connectionBuilder, subscription);
                    //var receiveClient = new Microsoft.Azure.ServiceBus.QueueClient(serviceBusConnectionString, subscription);
                    //var topicClient = new Microsoft.Azure.ServiceBus.TopicClient(serviceBusConnectionString, subscription);
                    //topicClient.
                    //var updatedSubscription = nsmgr.GetSubscription(subscription, subscription);
                    //var count = updatedSubscription.MessageCount;
                    //var receiveClient = new Microsoft.Azure.ServiceBus.QueueClient(serviceBusConnectionString, subscription);
                    //do
                    //{
                    //    status = topics
                    //        .Select(
                    //            topic =>
                    //            {
                    //                var subscriptions = nsmgr.GetSubscriptions(topic.Path);
                    //                foreach (var subscription in subscriptions)
                    //                {
                    //                    var updatedSubscription = nsmgr.GetSubscription(topic.Path, subscription.Name);
                    //                    var count = updatedSubscription.MessageCount;

                    //                    // While this would make more sense, it does not work:
                    //                    // https://stackoverflow.com/questions/43607450/azure-service-bus-messagecountdetails-activemessagecount-is-incorrect
                    //                    // var count = subscription.MessageCountDetails.ActiveMessageCount;

                    //                    if (count > 0)
                    //                        return true;
                    //                }
                    //                return false;
                    //            })
                    //        .Where(b => b)
                    //        .ToArray();
                    //}
                    //while (status.Any());
                    return 1;
                },
                (why) => -1);
        }

    }
}