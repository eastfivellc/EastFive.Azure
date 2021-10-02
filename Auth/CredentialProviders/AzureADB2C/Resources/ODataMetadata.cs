using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.AzureADB2C.Resources
{
    public class ODataMetadata<TData>
    {
        [JsonProperty("odata.metadata")]
        public string Metadata { get; set; }

        [JsonProperty("value")]
        public TData Value { get; set; }
    }
}
