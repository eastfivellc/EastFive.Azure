using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Api;
using EastFive.Api.Core;
using EastFive.Linq;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Monitoring
{
    public class TeamsNotificationExceptionHandlerAttribute : Attribute, IHandleExceptions, IHandleRoutes
    {
        bool deactivated;
        Uri teamsHookUrl;

        public TeamsNotificationExceptionHandlerAttribute() : base()
        {
            deactivated = AppSettings.ApplicationInsights.TeamsHook.ConfigurationUri(
                teamsHookUrl =>
                {
                    this.teamsHookUrl = teamsHookUrl;
                    return false;
                },
                (why) => true,
                () => true);
        }

        public async Task<IHttpResponse> HandleExceptionAsync(Exception ex, 
            MethodInfo method, KeyValuePair<ParameterInfo, object>[] queryParameters, 
            IApplication httpApp, IHttpRequest routeData,
            HandleExceptionDelegate continueExecution)
        {
            if (deactivated)
                return await continueExecution(ex, method, queryParameters,
                        httpApp, routeData);

            var message = await CreateMessageCardAsync(ex, method, httpApp, routeData);
            try
            {
                var response = await message.SendAsync(teamsHookUrl);
            } catch (HttpRequestException)
            {

            }
            return await continueExecution(ex, method, queryParameters,
                httpApp, routeData);
        }

        public async Task<IHttpResponse> HandleRouteAsync(Type controllerType,
            IApplication httpApp, IHttpRequest request,
            RouteHandlingDelegate continueExecution)
        {
            var response = await continueExecution(controllerType, httpApp, request);
            if (deactivated)
                return response;

            string teamsNotifyParam = GetTeamsNotifyParameter();
            
            if (!ShouldNotify())
                return response;

            try
            {
                string messageId = await TeamsNotifyAsync(response, teamsNotifyParam,
                    httpApp, request);
            } catch(HttpRequestException)
            {

            }
            return response;

            string GetTeamsNotifyParameter()
            {
                return request.Headers
                    .Where(kvp => kvp.Key.Equals("X-Teams-Notify", StringComparison.OrdinalIgnoreCase))
                    .First(
                        (teamsNotifyParams, next) =>
                        {
                            return teamsNotifyParams.Value.First(
                                (teamsNotifyParam, next) => teamsNotifyParam,
                                () => default(string));
                        },
                        () =>
                        {
                            return default(string);
                        });
            }

            bool HasReportableError()
            {
                if (((int)response.StatusCode) < 400)
                    return false;
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return false;
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                    return false;
                return true;
            }

            bool RequestTeamsNotify() => teamsNotifyParam != default;
            bool ShouldNotify()
            {
                if (RequestTeamsNotify())
                    return true;
                if (HasReportableError())
                    return true;
                if (TeamsNotification.IsMatch(response))
                    return true;

                return false;
            }
        }

        public async Task<string> TeamsNotifyAsync(IHttpResponse response, string teamsNotifyParam,
            IApplication httpApp, IHttpRequest request)
        {
            var message = await CreateMessageCardAsync(
                teamsNotifyParam, $"{request} = {response.StatusCode} / {response.ReasonPhrase}",
                httpApp, request,
                () => new MessageCard.Section
                {
                    title = "Request/Response Information",
                    markdown = false, // so that underscores are not stripped
                    facts = new MessageCard.Section.Fact[]
                    {
                        new MessageCard.Section.Fact
                        {
                            name = "Route Name:",
                            value = "TODO",
                        },
                                new MessageCard.Section.Fact
                                {
                                    name = "Http Method:",
                                    value = request.Method.Method,
                                },
                                new MessageCard.Section.Fact
                                {
                                    name = "URL:",
                                    value = request.GetAbsoluteUri().OriginalString,
                                },
                                new MessageCard.Section.Fact
                                {
                                    name = "Status Code:",
                                    value = $"{response.StatusCode.ToString()} / {(int)response.StatusCode}",
                                },
                                new MessageCard.Section.Fact
                                {
                                    name = "Reason:",
                                    value = response.ReasonPhrase,
                                },
                    }
                });

            return await message.SendAsync(teamsHookUrl);
        }


        //public Task<HttpResponseMessage> HandleMethodAsync(MethodInfo method,
        //        KeyValuePair<ParameterInfo, object>[] queryParameters,
        //        IApplication httpApp, HttpRequestMessage request,
        //    MethodHandlingDelegate continueExecution)
        //{
        //    if (!request.Headers.Contains("X-Teams-Notify"))
        //        return continueExecution(method, queryParameters, httpApp, request);

        //    var teamsNotifyParams = request.Headers.GetValues("X-Teams-Notify");
        //    if(!teamsNotifyParams.Any())
        //        return continueExecution(method, queryParameters, httpApp, request);

        //    var teamsNotifyParam = teamsNotifyParams.First();

        //    return AppSettings.ApplicationInsights.TeamsHook.ConfigurationUri(
        //        async teamsHookUrl =>
        //        {
        //            var response = continueExecution(method, queryParameters, httpApp, request);
        //            var message = await CreateMessageCardAsync(method, teamsNotifyParam,   httpApp, request);

        //            using (var client = new HttpClient())
        //            {
        //                var teamsRequest = new HttpRequestMessage(HttpMethod.Post, teamsHookUrl);
        //                var messageString = JsonConvert.SerializeObject(message);
        //                teamsRequest.Content = new StringContent(messageString);
        //                teamsRequest.Content.Headers.ContentType =
        //                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        //                var response = await client.SendAsync(teamsRequest);
        //                var responseMessage = await response.Content.ReadAsStringAsync();
        //            }
        //            return await continueExecution(ex, method, queryParameters,
        //                httpApp, request);
        //        },
        //        (why) => continueExecution(ex, method, queryParameters,
        //                httpApp, request));
        //}

        private static async Task<MessageCard> CreateMessageCardAsync(Exception ex, MethodInfo method,
            IApplication httpApp, IHttpRequest request)
        {
       
            var messageCard = await CreateMessageCardAsync(method, ex.Message,
                summary:"Server Exception", httpApp, request);

            var stackTrackSection = new MessageCard.Section
            {
                title = "Stack Trace",
                text = $"<blockquote>{ex.StackTrace.Replace("\r\n", "<p />")}</blockquote>",
            };
            messageCard.sections = messageCard.sections
                .Append(stackTrackSection)
                .ToArray();

            var vsoBugAction =
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
                    };
            messageCard.potentialAction = messageCard.potentialAction
                .Append(vsoBugAction)
                .ToArray();

            return messageCard;
        }

        private static Task<MessageCard> CreateMessageCardAsync(MethodInfo method,
             string title, string summary,
             IApplication httpApp, IHttpRequest request)
        {
            return CreateMessageCardAsync(title, summary, httpApp, request,
                () => new MessageCard.Section
                {
                    title = "Request Information",
                    markdown = false, // so that underscores are not stripped
                    facts = new MessageCard.Section.Fact[]
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
                                value = request.GetAbsoluteUri().OriginalString,
                            },
                            new MessageCard.Section.Fact
                            {
                                name = "Reason:",
                                value = summary,
                            },
                        }
                });
        }

        private static async Task<MessageCard> CreateMessageCardAsync(
             string title, string summary,
             IApplication httpApp, IHttpRequest request,
             Func<MessageCard.Section> getRequestInformation)
        {
            var appName = AppSettings.ApplicationInsights.TeamsAppIdentification
                .ConfigurationString(
                    x => x,
                    (why) => httpApp.GetType().FullName);
            var appImage = AppSettings.ApplicationInsights.TeamsAppImage
                .ConfigurationString(
                    x => new Uri(x),
                    (why) => default);
            var content = await ReadContentAsync();

            var utcOffset = "Central Standard Time"
                .FindSystemTimeZone()
                .GetUtcOffset(DateTime.UtcNow);

            var sections = new MessageCard.Section[]
            {
                new MessageCard.Section
                {
                        activityTitle = appName,
                        activitySubtitle = (DateTime.UtcNow + utcOffset).ToString("f"),
                        activityImage = appImage,
                },
                getRequestInformation(),
                new MessageCard.Section
                {
                        title = "Headers",
                        markdown = false, // so that underscores are not stripped
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
                }
            };

            if (request.Properties.ContainsKey(HttpApplication.DiagnosticsLogProperty))
            {
                var diagnosticsLogs = (string[])request.Properties[HttpApplication.DiagnosticsLogProperty];
                sections = sections
                    .Append(
                        new MessageCard.Section
                        {
                            title = "Log",
                            text = $"<blockquote>{diagnosticsLogs.Join("<br />")}</blockquote>",
                        })
                    .ToArray();
            }

            var message = new MessageCard
            {
                summary = summary,
                themeColor = "F00807",
                title = title,
                sections = sections,
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
                }
            };
            return message;

            async Task<string> ReadContentAsync()
            {
                if (!request.HasBody) 
                    return string.Empty;

                try
                {
                    if (request.RequestHeaders.ContentType.IsTextType())
                        return await request.ReadContentAsStringAsync();

                    var byteContent = await request.ReadContentAsync();
                    return byteContent.ToBase64String();
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            }
        }
    }
}
