
using EastFive.Extensions;
using Microsoft.Extensions.Logging;

namespace EastFive.Analytics
{
    public class AnalyticsLogger : EastFive.Analytics.ILogger
    {
        private Microsoft.Extensions.Logging.ILogger logger;
        private object loggerLock = new object();

        public AnalyticsLogger(Microsoft.Extensions.Logging.ILogger logger)
        {
            this.logger = logger;
        }

        public void LogInformation(string message)
        {
            if (logger.IsDefaultOrNull())
                return;
            lock (loggerLock)
                logger.LogInformation(message);
        }

        public void LogTrace(string message)
        {
            if (logger.IsDefaultOrNull())
                return;
            lock (loggerLock)
                logger.LogInformation(message);
        }

        public void LogWarning(string message)
        {
            if (logger.IsDefaultOrNull())
                return;
            lock (loggerLock)
                logger.LogWarning(message);
        }

        public void LogCritical(string message)
        {
            if (logger.IsDefaultOrNull())
                return;
            lock (loggerLock)
                logger.LogCritical(message);
        }
    }
}
