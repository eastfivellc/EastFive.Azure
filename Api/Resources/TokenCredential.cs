﻿using System;
using System.Linq;
using System.Runtime.Serialization;
using BlackBarLabs.Api.Resources;
using Newtonsoft.Json;

namespace EastFive.Api.Azure.Credentials.Resources
{
    [DataContract]
    public class TokenCredential : BlackBarLabs.Api.ResourceBase
    {
        #region Properties
        
        [DataMember]
        [JsonProperty("actor")]
        public WebId Actor { get; set; }
        
        [DataMember]
        [JsonProperty("email")]
        public string Email { get; set; }

        [DataMember]
        [JsonProperty(PropertyName = "last_email_sent")]
        public DateTime? LastEmailSent { get; set; }

        #endregion
    }
}
