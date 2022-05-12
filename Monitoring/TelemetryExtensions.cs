using EastFive.Web.Configuration;
using Microsoft.ApplicationInsights;

namespace EastFive.Azure.Monitoring
{
    public static class TelemetryExtensions
    {
        private static readonly TelemetryClient client;

        static TelemetryExtensions()
        {
            client = EastFive.Azure.AppSettings.ApplicationInsights.InstrumentationKey.ConfigurationString(
                key =>
                {
                    return new TelemetryClient { InstrumentationKey = key };
                },
                (why) =>
                {
                    return new TelemetryClient();
                });
        }

        public static TelemetryClient LoadTelemetryClient(this string configKeyName)
        {
            // sharing this instance so that we don't leak handles/threads from repeated instantiations
            return client;
        }
    }
}
