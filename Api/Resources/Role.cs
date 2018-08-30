﻿using System.Runtime.Serialization;
using BlackBarLabs.Api;
using Newtonsoft.Json;
using BlackBarLabs.Api.Resources;

namespace EastFive.Api.Azure.Resources
{
    [DataContract]
    public class Role : ResourceBase
    {
        [JsonProperty(PropertyName = "actor")]
        public WebId Actor { get; set; }

        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }
    }
}