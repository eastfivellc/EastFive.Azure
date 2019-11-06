using BlackBarLabs.Extensions;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Functions
{
    public static class ResourceQueryCompilationExtensions
    {
        public static async Task<InvocationMessage> FunctionAsync(this HttpRequestMessage request,
            AzureApplication application = default)
        {
            var invocationMessage = await request.InvocationMessageAsync();
            var invocationMessageRef = invocationMessage.invocationRef;
            return await await invocationMessage.StorageCreateAsync(
                async (created) =>
                {
                    var byteContent = invocationMessageRef.id.ToByteArray();
                    AzureApplication GetApplication()
                    {
                        if (!application.IsDefaultOrNull())
                            return application;
                        var app = new AzureApplication();
                        app.ApplicationStart();
                        return app;
                    }
                    var applicationValid = GetApplication();

                    return await EastFive.Web.Configuration.Settings.GetString(
                        AppSettings.FunctionProcessorServiceBusTriggerName,
                        async (serviceBusTriggerName) =>
                        {
                            await applicationValid.SendServiceBusMessageAsync(serviceBusTriggerName, byteContent);
                            return invocationMessage;
                        },
                        (why) => throw new Exception(why));
                },
                () => throw new Exception());
        }
    }
}
