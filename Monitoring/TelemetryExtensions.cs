using EastFive.Web.Configuration;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;

namespace EastFive.Azure.Monitoring
{
    public static class TelemetryExtensions
    {
        private static readonly TelemetryClient client;

        static TelemetryExtensions()
        {
            
            client = EastFive.Azure.AppSettings.ApplicationInsights.ConnectionString.ConfigurationString(
                key =>
                {
                    var config = new TelemetryConfiguration()
                    {
                        ConnectionString = key,
                    };
                    return new TelemetryClient(config);
                },
                (why) =>
                {
#pragma warning disable 0618
                    var configuration = TelemetryConfiguration.Active;
                    return new TelemetryClient(configuration);
                });
        }

        public static TelemetryClient LoadTelemetryClient()
        {
            // sharing this instance so that we don't leak handles/threads from repeated instantiations
            return client;
        }
    }
}
