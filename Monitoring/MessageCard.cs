using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Monitoring
{
    public struct MessageCard
    {
        [JsonProperty(PropertyName ="@type")]
        public string type => "MessageCard";

        [JsonProperty(PropertyName = "@context")]
        public string context => "http://schema.org/extensions";

        public string summary;

        public string themeColor;

        public struct Section
        {
            public string activityTitle;
            public string activitySubtitle;
            public Uri activityImage;

            public string title;
            public string text;
            public bool markdown;

            public struct Fact
            {
                public string name;
                public string value;
            }

            public Fact[] facts;
        }

        public Section[] sections;

        public struct ActionCard
        {
            [JsonProperty(PropertyName = "@type")]
            public string type;

            public string name;

            public struct Input
            {
                [JsonProperty(PropertyName = "@type")]
                public string type;

                public string id;
                public bool? isMultiline;
                public string title;
                public bool? isMultiSelect;
                public string name;
                public Uri target;
                public Choice[] choices;

                public struct Choice
                {
                    public string display;
                    public string value;
                }
            }

            public Input[] inputs;

            public struct Action
            {
                [JsonProperty(PropertyName = "@type")]
                public string type;
                public string name;
                public Uri target;
            }
            public Action[] actions;

            public struct Target
            {
                public string os;
                public Uri uri;
            }
            public Target[] targets;
        }

        public ActionCard[] potentialAction;

        public async Task<string> SendAsync(Uri teamsHookUrl)
        {
            using (var client = new HttpClient())
            {
                using (var teamsRequest = new HttpRequestMessage(HttpMethod.Post, teamsHookUrl))
                {
                    var messageString = JsonConvert.SerializeObject(this);
                    teamsRequest.Content = new StringContent(messageString);
                    teamsRequest.Content.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                    using (var response = await client.SendAsync(teamsRequest))
                    {
                        var responseMessage = await response.Content.ReadAsStringAsync();
                        return responseMessage;
                    }
                }
            }
        }
    }
}
