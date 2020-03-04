using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.AzureADB2C.Resources
{
    public class ConfigurationResource
    {
        public const string ConfigurationEndpoint = "https://{0}.b2clogin.com/{0}.onmicrosoft.com/v2.0/.well-known/openid-configuration?p={1}";

        [JsonProperty(PropertyName = "issuer")]
        public string Issuer { get; set; }

        [JsonProperty(PropertyName = "authorization_endpoint")]
        public string AuthorizationEndpoint { get; set; }

        [JsonProperty(PropertyName = "end_session_endpoint")]
        public string EndSessionEndpoint { get; set; }
        
        [JsonProperty(PropertyName = "token_endpoint")]
        public string TokenEndpoint { get; set; }

        [JsonProperty(PropertyName = "jwks_uri")]
        public string JwksUri { get; set; }

        [JsonProperty(PropertyName = "response_modes_supported")]
        public string [] ResponseModesSupported { get; set; }

        [JsonProperty(PropertyName = "claims_supported")]
        public string [] ClaimsSupported { get; set; }
    }
}
