using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Auth
{
    public interface IProvideIntegration
    {
        string GetDefaultName(IDictionary<string, string> extraParams);

        Task<bool> SupportsIntegrationAsync(Guid accountId);

    }
}
