using BlackBarLabs.Extensions;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Serialization;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Functions
{
    public class InvokeFunction : InvokeApplication
    {
        public override IApplication Application => azureApplication;

        private IAzureApplication azureApplication;

        private int executionLimit = 1;

        public InvokeFunction(IAzureApplication application, Uri serverUrl, string apiRouteName, int executionLimit = 1)
            : base(serverUrl, apiRouteName)
        {
            IAzureApplication GetApplication()
            {
                if (application is FunctionApplication)
                    return application;
                var newApp = Activator.CreateInstance(application.GetType()) as IAzureApplication;
                //newApp.ApplicationStart();
                return newApp;
            }
            this.azureApplication = GetApplication();
            this.executionLimit = executionLimit;
        }

        public override Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequest)
        {
            return InvocationMessage.CreateAsync(httpRequest, executionLimit:executionLimit);
        }
    }
}
