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
using EastFive.Api.Azure.Monitoring;
using EastFive.Api.Diagnositcs;
using EastFive.Diagnostics;

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

        public async Task<IHttpResponse> HandleRouteAsync(Type controllerType, IInvokeResource resourceInvoker,
            IApplication httpApp, IHttpRequest request,
            RouteHandlingDelegate continueExecution)
        {
            var response = await continueExecution(controllerType, httpApp, request);
            if (deactivated)
                return response;

            string teamsNotifyParam = GetTeamsNotifyParameter();
            var isDone = !ShouldNotify(out string collectionFolder);
            var monitoringRequest = await Api.Azure.Monitoring.MonitoringRequest.CreateAsync(
                controllerType, resourceInvoker,
                httpApp, request, collectionFolder, response);

            if (isDone)
                return response;

            try
            {
                string messageId = await TeamsNotifyAsync(controllerType, resourceInvoker,
                    httpApp, request, response,
                    teamsNotifyParam, collectionFolder, monitoringRequest);
            } catch(HttpRequestException)
            {

            } catch(Exception)
            {

            }
            return response;
            
            string GetTeamsNotifyParameter()
            {
                return request.Headers
                    .Where(kvp => kvp.Key.Equals("X-Teams-Notify", StringComparison.OrdinalIgnoreCase))
                    .Where(kvp => kvp.Value.Any(v => !"Allscripts".Equals(v)))
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
            bool ShouldNotify(out string collectionFolder)
            {
                collectionFolder = default;
                if (RequestTeamsNotify())
                    return true;
                if (HasReportableError())
                    return true;
                if (TeamsNotification.IsMatch(request, response, out collectionFolder))
                    return true;

                return false;
            }
        }

        public async Task<string> TeamsNotifyAsync(Type controllerType, IInvokeResource resourceInvoker,
            IApplication httpApp, IHttpRequest request, IHttpResponse response,
            string teamsNotifyParam, string collectionFolder, MonitoringRequest monitoringRequest)
        {
            var monitoringRequestId = monitoringRequest.id.ToString();

            var responseParam = response.Headers
                .Where(hdr => hdr.Key == Middleware.HeaderStatusName)
                .Where(hdr => hdr.Value.AnyNullSafe())
                .First(
                    (hdr, next) => hdr.Value.First(),
                    () => "");

            // add more info to first activity card because of the "See more" rollup the Teams app does
            var segments = request.RequestUri.Segments
                .Where(x => !Guid.TryParse(x, out Guid id)) // omit guids to create a shorter title
                .Join("");
            var message = await CreateMessageCardAsync(
                segments, $"{(int)response.StatusCode} (Reason: {response.ReasonPhrase})", //teamsNotifyParam,
                monitoringRequest,
                httpApp, request,
                () =>
                {
                    var cardSection = new MessageCard.Section
                    {
                        title = "Request/Response Information",
                        markdown = false, // so that underscores are not stripped
                        facts = new MessageCard.Section.Fact[]
                        {
                            new MessageCard.Section.Fact
                            {
                                name = "Response Param:",
                                value = responseParam,
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
                                value = $"{response.StatusCode} / {(int)response.StatusCode}",
                            },
                            new MessageCard.Section.Fact
                            {
                                name = "Reason:",
                                value = response.ReasonPhrase,
                            },
                            new MessageCard.Section.Fact
                            {
                                name = "MonitoringRequest:",
                                value = monitoringRequestId,
                            },
                        },
                    };
                    return cardSection;
                });

            return await message.SendAsync(teamsHookUrl);
        }


        //public Task<HttpResponseMessage> HandleMethodAsync(MethodInfo method,
        //        KeyValuePair<ParameterInfo, object>[] queryParameters,
        //        IApplication httpApp, HttpRequestMessage request,
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
                text = ex.StackTrace.Replace("\n", "<br />"),
            };
            messageCard.sections = messageCard.sections
                .Append(stackTrackSection)
                .ToArray();

            //var vsoBugAction =
            //        new MessageCard.ActionCard
            //        {
            //            type = "ActionCard",
            //            name = "Create Bug in Visual Studio Online",
            //            inputs = new MessageCard.ActionCard.Input[]
            //            {
            //                new MessageCard.ActionCard.Input
            //                {
            //                    type = "TextInput",
            //                    id = "bugtitle",
            //                    title = "Title",
            //                    isMultiline = false,
            //                },
            //                new MessageCard.ActionCard.Input
            //                {
            //                    type = "DateInput",
            //                    id = "dueDate",
            //                    title = "Select a date",
            //                },
            //                new MessageCard.ActionCard.Input
            //                {
            //                    type = "TextInput",
            //                    id = "comment",
            //                    title = "Enter your comment",
            //                    isMultiline = true,
            //                },
            //            }
            //        };
            //messageCard.potentialAction = messageCard.potentialAction
            //    .Append(vsoBugAction)
            //    .ToArray();

            return messageCard;
        }

        private static Task<MessageCard> CreateMessageCardAsync(MethodInfo method,
             string title, string summary,
             IApplication httpApp, IHttpRequest request)
        {
            return CreateMessageCardAsync(title, summary,
                    default(MonitoringRequest),
                    httpApp, request,
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
             string title, string summary, MonitoringRequest monitoringRequest,
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
                    activityTitle = title,
                    activitySubtitle = summary,
                    activityImage = appImage,
                    markdown = false,
                },
                getRequestInformation(),
                new MessageCard.Section
                {
                    title = "Headers",
                    markdown = false, // so that underscores are not stripped
                    facts =  request.Headers
                        .Where(x => !x.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                        .Select(
                            header => new MessageCard.Section.Fact
                            {
                                name = $"{header.Key}:",
                                value = header.Value.Join(","),
                            })
                        .ToArray(),
                },
            };

            // put authorization header in own section for more compact viewing on mobile
            var authorizations = request.Headers.Where(x => x.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (authorizations.Any())
            {
                sections = sections
                    .Append(
                        new MessageCard.Section
                        {
                            title = "Authorization",
                            text = authorizations[0].Value.Join(","),
                            markdown = false,
                        })
                    .ToArray();
            }

            if (content.HasBlackSpace())
            {
                sections = sections
                    .Append(
                        new MessageCard.Section
                        {
                            title = "Content",
                            text = $"<blockquote>{content}</blockquote>",
                        })
                    .ToArray();
            }

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

            if (request.Properties.TryGetValue(EnableProfilingAttribute.ResponseProperty, out object profileObj))
            {
                var profile = (IProfile)profileObj;
                var log = profile.Events
                    .NullToEmpty()
                    .OrderBy(kvp => kvp.Key)
                    .Select(diagEvent => $"<b>{diagEvent.Key}</b>:<i>{diagEvent.Value}</i>")
                    .Join("<br />");
                sections = sections
                    .Append(
                        new MessageCard.Section
                        {
                            title = "Diagnostics",
                            text = $"<blockquote>{log}</blockquote>",
                        })
                    .ToArray();
            }

            var actions = new MessageCard.ActionCard[] { };
            if (monitoringRequest != null)
            {
                var offset = EastFive.Api.AppSettings.AccessTokenExpirationInMinutes.ConfigurationDouble(
                    (minutes) => TimeSpan.FromMinutes(minutes));
                var expiresOn = DateTime.UtcNow + offset;
                var postmanLink = new QueryableServer<Api.Azure.Monitoring.MonitoringRequest>(request)
                    .Where(mr => mr.monitoringRequestRef == monitoringRequest.monitoringRequestRef)
                    .Where(mr => mr.when == monitoringRequest.when.Date)
                    .HttpAction(MonitoringRequest.PostmanAction)
                    .Location()
                    .SignWithAccessTokenAccount(Guid.NewGuid(), Guid.NewGuid(), expiresOn,
                        url => url);
                actions = actions
                    .Append(
                        new MessageCard.ActionCard
                        {
                            name = "Add to Postman Collection",
                            type = "OpenUri",
                            targets = new MessageCard.ActionCard.Target[]
                            {
                                new MessageCard.ActionCard.Target
                                {
                                    os = "default",
                                    uri = postmanLink,
                                }
                            }
                        })
                    .ToArray();
            }

            var message = new MessageCard
            {
                summary = appName,
                themeColor = "F00807",
                sections = sections,
                potentialAction = actions,
                    //new MessageCard.ActionCard
                    //{
                    //    name = "Application Insights",
                    //    type = "OpenUri",
                    //    targets = new MessageCard.ActionCard.Target[]
                    //    {
                    //        new MessageCard.ActionCard.Target
                    //        {
                    //            os = "default",
                    //            uri = new Uri("https://www.example.com/ai/message/1234"),
                    //        }
                    //    }
                    //},
                    //new MessageCard.ActionCard
                    //{
                    //    type = "ActionCard",
                    //    name = "Run in TestFramework",
                    //    inputs = new MessageCard.ActionCard.Input[]
                    //    {
                    //        new MessageCard.ActionCard.Input
                    //        {
                    //            type = "TextInput",
                    //            id = "comment",
                    //            title = "Test ID",
                    //            isMultiline = false,
                    //        },
                    //        new MessageCard.ActionCard.Input
                    //        {
                    //            type = "MultichoiceInput",
                    //            id = "move",
                    //            title = "Pick a test function",
                    //            isMultiSelect = false,
                    //            choices = new MessageCard.ActionCard.Input.Choice []
                    //            {
                    //                new MessageCard.ActionCard.Input.Choice
                    //                {
                    //                    display = "Unauthenticated",
                    //                    value = "unauthenticated",
                    //                },
                    //                new MessageCard.ActionCard.Input.Choice
                    //                {
                    //                    display = "Physicial Redirect",
                    //                    value = "redirect",
                    //                },
                    //                new MessageCard.ActionCard.Input.Choice
                    //                {
                    //                    display = "Session Authenticated",
                    //                    value = "session",
                    //                },
                    //                new MessageCard.ActionCard.Input.Choice
                    //                {
                    //                    display = "Account Authenticated",
                    //                    value = "account",
                    //                },
                    //            },
                    //        }
                    //    }
                    //},
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
