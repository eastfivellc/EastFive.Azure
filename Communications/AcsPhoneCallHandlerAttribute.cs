using System;
using System.Threading.Tasks;
using EastFive.Linq.Async;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using System.Linq;
using EastFive.Linq;

namespace EastFive.Azure.Communications;

public class AcsPhoneCallHandlerAttribute : IncomingCallHandlerAttribute
{
        /// <summary>
        /// Handles an incoming call by answering and forwarding to the configured target.
        /// </summary>
        protected override async Task<IHttpResponse> HandleIncomingCallAsync(
            string incomingCallContext, string toPhoneNumber, string fromPhoneNumber, string correlationId,
            AcsPhoneNumber? acsPhoneNumberMaybe, IHttpRequest request, HttpApplication httpApp,
            Func<Task<IHttpResponse>> continueExecution)
        {
            // Get the forwarding number for this ACS phone number
            if (!acsPhoneNumberMaybe.TryGetValue(out var acsPhoneNumber))
                return await continueExecution();
            
            return await httpApp.GetType()
                .GetAttributesInterface<IProcessCallEvent>(inherit: true, multiple: true)
                .First(
                    async (handler, next) =>
                    {
                        return await await fromPhoneNumber
                            .StorageGetBy(
                                (AcsPhoneCall acsPhoneCall) => acsPhoneCall.listeningParticipantPhoneNumber,
                                query1 => query1.conferencePhoneNumber == acsPhoneNumber.acsPhoneNumberRef)
                            .SingleAsync(
                                async (phoneCall) =>
                                {
                                    // Answer the call and forward it

                                    return await await handler.HandleParticipantCallingAsync(
                                            phoneCall,
                                            incomingCallContext,
                                            toPhoneNumber,
                                            fromPhoneNumber,
                                            correlationId,
                                            acsPhoneNumber,
                                            request, httpApp,
                                        phoneCall => continueExecution(),
                                        errorMsg =>
                                        {
                                            return continueExecution();
                                        });
                                },
                                async (phoneCalls) =>
                                {
                                    return await phoneCalls
                                        .OrderByDescending(pc => pc.lastModified)
                                        .First(
                                            async (phoneCall, next) =>
                                            {
                                                var cleanupTask = phoneCalls
                                                    .Where(pc => pc.id != phoneCall.id)
                                                    .Select(
                                                        async pc =>
                                                        {
                                                            var pcUpdated = await pc.acsPhoneCallRef.StorageUpdateAsync(
                                                                async (current, saveAsync) =>
                                                                {
                                                                    current.listeningParticipantPhoneNumber = null;
                                                                    await saveAsync(current);
                                                                    return current;
                                                                },
                                                                () => pc);
                                                            return pcUpdated;
                                                        })
                                                    .WhenAllAsync();

                                                return await await handler.HandleParticipantCallingAsync(
                                                        phoneCall,
                                                        incomingCallContext,
                                                        toPhoneNumber,
                                                        fromPhoneNumber,
                                                        correlationId,
                                                        acsPhoneNumber,
                                                        request, httpApp,
                                                    phoneCall => continueExecution(),
                                                    errorMsg =>
                                                    {
                                                        return continueExecution();
                                                    });
                                            },
                                            () => continueExecution());
                                },
                                async () =>
                                {
                                    // No queued call found - pass to next handler in chain
                                    return await continueExecution();
                                });

                    },
                    () => request
                        .CreateResponse(System.Net.HttpStatusCode.NotFound)
                        .AddReason("No IProcessCallEvent handler found. Please add an attribute implementing IProcessCallEvent to the HttpApplication.")
                        .AsTask());
        }
}
