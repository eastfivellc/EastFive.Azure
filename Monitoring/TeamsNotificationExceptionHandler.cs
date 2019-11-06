using EastFive.Api;
using EastFive.Extensions;
using EastFive.Web.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Monitoring
{
    public class TeamsNotificationExceptionHandlerAttribute : Attribute, IHandleExceptions
    {
        public async Task<HttpResponseMessage> HandleExceptionAsync(Exception ex, 
            MethodInfo method, KeyValuePair<ParameterInfo, object>[] queryParameters, 
            IApplication httpApp, HttpRequestMessage request,
            HandleExceptionDelegate continueExecution)
        {
            return await AppSettings.ApplicationInsights.TeamsHook.ConfigurationUri(
                async teamsHookUrl =>
                {
                    var message = await CreateMessageCardAsync(ex, method, httpApp, request);

                    using (var client = new HttpClient())
                    {
                        var teamsRequest = new HttpRequestMessage(HttpMethod.Post, teamsHookUrl);
                        var messageString = JsonConvert.SerializeObject(message);
                        teamsRequest.Content = new StringContent(messageString);
                        teamsRequest.Content.Headers.ContentType =
                            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                        var response = await client.SendAsync(teamsRequest);
                        var responseMessage = await response.Content.ReadAsStringAsync();
                    }
                    return await continueExecution(ex, method, queryParameters,
                        httpApp, request);
                },
                (why) => continueExecution(ex, method, queryParameters,
                        httpApp, request));
        }

        private static async Task<MessageCard> CreateMessageCardAsync(Exception ex, MethodInfo method,
            IApplication httpApp, HttpRequestMessage request)
        {
            var appName = AppSettings.ApplicationInsights.TeamsAppIdentification
                .ConfigurationString(
                    x => x,
                    (why) => httpApp.GetType().FullName);
            var appImage = AppSettings.ApplicationInsights.TeamsAppImage
                .ConfigurationString(
                    x => new Uri(x),
                    (why) =>default);
            var content = string.Empty;
            if (!request.Content.IsDefaultOrNull())
            {
                //var contentData = await request.Content.ReadAsByteArrayAsync();
                content = await request.Content.ReadAsStringAsync();
            }
            var utcOffset = TimeZoneInfo
                .FindSystemTimeZoneById("Central Standard Time")
                .GetUtcOffset(DateTime.UtcNow);
            var message = new MessageCard
            {
                summary = "Server Exception",
                themeColor = "F00807",
                title = ex.Message,
                sections = new MessageCard.Section[]
                {
                    new MessageCard.Section
                    {
                        activityTitle = appName,
                        activitySubtitle = (DateTime.UtcNow + utcOffset).ToString("f"),
                        activityImage = appImage,
                    },
                    new MessageCard.Section
                    {
                        title = "Request Information",
                        facts =  new MessageCard.Section.Fact[]
                        {
                            new MessageCard.Section.Fact
                            {
                                name = "Resource:",
                                value = method.DeclaringType.FullName,
                            },
                            new MessageCard.Section.Fact
                            {
                                name = "Http Method:",
                                value = request.Method.Method,
                            },
                            new MessageCard.Section.Fact
                            {
                                name = "Method:",
                                value = method.Name,
                            },
                            new MessageCard.Section.Fact
                            {
                                name = "URL",
                                value = request.RequestUri.OriginalString,
                            }
                        }
                    },
                    new MessageCard.Section
                    {
                        title = "Headers",
                        facts =  request.Headers
                            .Select(
                                header => new MessageCard.Section.Fact
                                {
                                    name = $"{header.Key}:",
                                    value = header.Value.Join(","),
                                })
                            .ToArray(),
                    },
                    new MessageCard.Section
                    {
                        title = "Content",
                        text = $"<blockquote>{content}</blockquote>",
                    },
                    new MessageCard.Section
                    {
                        title = "Stack Trace",
                        text = $"<blockquote>{ex.StackTrace.Replace("\r\n", "<p />")}</blockquote>",
                    },
                },
                potentialAction = new MessageCard.ActionCard[]
                {
                    new MessageCard.ActionCard
                    {
                        name = "Application Insights",
                        type = "OpenUri",
                        targets = new MessageCard.ActionCard.Target[]
                        {
                            new MessageCard.ActionCard.Target
                            {
                                os = "default",
                                uri = new Uri("https://www.example.com/ai/message/1234"),
                            }
                        }
                    },
                    new MessageCard.ActionCard
                    {
                        name = "Postman",
                        type = "OpenUri",
                        targets = new MessageCard.ActionCard.Target[]
                        {
                            new MessageCard.ActionCard.Target
                            {
                                os = "default",
                                uri = new Uri("https://www.example.com/ai/message/1234"),
                            }
                        }
                    },
                    new MessageCard.ActionCard
                    {
                        type = "ActionCard",
                        name = "Run in TestFramework",
                        inputs = new MessageCard.ActionCard.Input[]
                        {
                            new MessageCard.ActionCard.Input
                            {
                                type = "TextInput",
                                id = "comment",
                                title = "Test ID",
                                isMultiline = false,
                            },
                            new MessageCard.ActionCard.Input
                            {
                                type = "MultichoiceInput",
                                id = "move",
                                title = "Pick a test function",
                                choices = new MessageCard.ActionCard.Input.Choice []
                                {
                                    new MessageCard.ActionCard.Input.Choice
                                    {
                                        display = "Unauthenticated",
                                        value = "unauthenticated",
                                    },
                                    new MessageCard.ActionCard.Input.Choice
                                    {
                                        display = "Physicial Redirect",
                                        value = "redirect",
                                    },
                                    new MessageCard.ActionCard.Input.Choice
                                    {
                                        display = "Session Authenticated",
                                        value = "session",
                                    },
                                    new MessageCard.ActionCard.Input.Choice
                                    {
                                        display = "Account Authenticated",
                                        value = "account",
                                    },
                                },
                            }
                        }
                    },
                    new MessageCard.ActionCard
                    {
                        type = "ActionCard",
                        name = "Create Bug in Visual Studio Online",
                        inputs = new MessageCard.ActionCard.Input[]
                        {
                            new MessageCard.ActionCard.Input
                            {
                                type = "TextInput",
                                id = "bugtitle",
                                title = "Title",
                                isMultiline = false,
                            },
                            new MessageCard.ActionCard.Input
                            {
                                type = "DateInput",
                                id = "dueDate",
                                title = "Select a date",
                            },
                            new MessageCard.ActionCard.Input
                            {
                                type = "TextInput",
                                id = "comment",
                                title = "Enter your comment",
                                isMultiline = true,
                            },
                        }
                    },
                }
            };
            return message;
        }
    }
}
