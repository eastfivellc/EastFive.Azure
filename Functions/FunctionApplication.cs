using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EastFive.Analytics;

namespace EastFive.Azure.Functions
{
    public interface IFunctionApplication
    {
        IRef<InvocationMessage> InvocationMessageRef { get; }
    }

    public class FunctionApplication : Api.Azure.AzureApplication
    {
        public ILogger logger;

        public FunctionApplication(ILogger logger)
        {
            this.logger = logger;
        }

        public override ILogger Logger => logger;

    }
}
