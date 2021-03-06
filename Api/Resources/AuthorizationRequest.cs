﻿using BlackBarLabs.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EastFive.Api.Azure.Credentials.Resources
{
    [DataContract]
    public class AuthorizationRequest : BlackBarLabs.Api.ResourceBase
    {
        [DataMember]
        [JsonProperty(PropertyName = "method")]
        public string Method { get; set; }

        [DataMember]
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [DataMember]
        [JsonProperty(PropertyName = "location_authentication")]
        public Uri LocationAuthentication { get; set; }

        [DataMember]
        [JsonProperty(PropertyName = "location_authentication_return")]
        public Uri LocationAuthenticationReturn { get; set; }

        [DataMember]
        [JsonProperty(PropertyName = "response_token")]
        public Dictionary<string, string> ResponseToken { get; set; }
        
        [DataMember]
        [JsonProperty(PropertyName = "extra_params")]
        public IDictionary<string, string> ExtraParams { get; set; }

        [JsonProperty(PropertyName = "user_parameters")]
        public IDictionary<string, CustomParameter> UserParameters { get; set; }

        [JsonProperty(PropertyName = "resource_types")]
        public ResourceType[] ResourceTypes { get; set; }
        
        [DataContract]
        public class CustomParameter
        {
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }

            [JsonProperty(PropertyName = "type")]
            public string Type { get; set; }

            [JsonProperty(PropertyName = "label")]
            public string Label { get; set; }

            [JsonProperty(PropertyName = "description")]
            public string Description { get; set; }
        }

        [DataContract]
        public class ResourceType
        {
            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "type")]
            public Uri Type { get; set; }

            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
        }
    }
}