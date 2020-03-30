using EastFive.Api;
using EastFive.Api.Azure;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace EastFive.Azure.Functions
{
    public class InvokeFunction : InvokeApplication, IDisposable
    {
        public override IApplication Application => azureApplication;

        private AzureApplication azureApplication;
        private int executionLimit = 1;
        private bool createdByThis;
        private bool disposed;

        public InvokeFunction(AzureApplication application, Uri serverUrl, string apiRouteName, int executionLimit = 1)
            : base(serverUrl, apiRouteName)
        {
            AzureApplication GetApplication()
            {
                if (application is FunctionApplication)
                    return application;
                var newApp = Activator.CreateInstance(application.GetType()) as AzureApplication;
                createdByThis = true;
                newApp.ApplicationStart();
                return newApp;
            }
            this.azureApplication = GetApplication();
            this.executionLimit = executionLimit;
        }

        public override Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequest)
        {
            return InvocationMessage.CreateAsync(httpRequest, executionLimit:executionLimit);
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
                if (createdByThis)
                    azureApplication.Dispose();
            }
            disposed = true;
        }
    }
}
