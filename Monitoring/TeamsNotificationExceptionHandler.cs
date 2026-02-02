using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;

using EastFive;
using EastFive.Api;
using EastFive.Api.Core;
using EastFive.Linq;
using EastFive.Web.Configuration;
using EastFive.Api.Azure.Monitoring;
using EastFive.Api.Diagnositcs;
using EastFive.Diagnostics;
using EastFive.Azure.Functions;

namespace EastFive.Azure.Monitoring
{
    public class TeamsNotificationExceptionHandlerAttribute : Attribute, IHandleExceptions, IHandleRoutes
    {
        readonly bool deactivated;
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

        public static string GetCardTitle(Uri requestUri) => 
            requestUri.Segments
                .Where(x => !Guid.TryParse(x, out Guid id)) // omit guids to create a shorter title
                .Join("");

        public static string GetCardSummary(IHttpResponse response)
        {
            var result = $"{(int)response.StatusCode}";
            if (response.ReasonPhrase.HasBlackSpace())
                result += $" (Reason: {response.ReasonPhrase})";
            else
                result += $" (Reason: {response.StatusCode.ToString()})";

            return result;
        }

        public static string GetCardSummary(Exception ex)
        {
            var result = $"{ex.GetType().FullName} (Message: {ex.Message})";
            return result;
        }

        public async Task<IHttpResponse> HandleExceptionAsync(Exception ex, 
            MethodInfo method, KeyValuePair<ParameterInfo, object>[] queryParameters, 
            IApplication httpApp, IHttpRequest request,
            HandleExceptionDelegate continueExecution)
        {
            if (deactivated)
                return await continueExecution(ex, method, queryParameters,
                        httpApp, request);

            var message = await CompileCardAsync(
                GetCardTitle(request.RequestUri), 
                GetCardSummary(ex),
                httpApp,
                default(MonitoringRequest), request, default(IHttpResponse), ex);
            var response = await message.SendAsync(teamsHookUrl);

            return await continueExecution(ex, method, queryParameters,
                httpApp, request);
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

            var message = await CompileCardAsync(
                GetCardTitle(request.RequestUri), 
                GetCardSummary(response),
                httpApp,
                monitoringRequest, request, response, default(Exception));
            string discardId = await message.SendAsync(teamsHookUrl);

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
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
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

        public static async Task<MessageCard> CompileCardAsync(
             string title, string summary, IApplication application, MonitoringRequest monitoringRequest,
             IHttpRequest request, IHttpResponse response, Exception ex,
             Func<MessageCard.Section> getAdditionalInformation = default)
        {
            var appName = AppSettings.ApplicationInsights.TeamsAppIdentification
                .ConfigurationString(
                    x => x,
                    (why) => "App Logger");
            var appImage = AppSettings.ApplicationInsights.TeamsAppImage
                .ConfigurationString(
                    x => new Uri(x),
                    (why) => default);

            var sections = new MessageCard.Section[]
            {
                new MessageCard.Section
                {
                    activityTitle = title,
                    activitySubtitle = summary,
                    activityImage = appImage,
                    markdown = false,
                    facts =
                    [
                        new MessageCard.Section.Fact
                        {
                            name = "Server",
                            value = getServerDescription(),
                        },
                        new MessageCard.Section.Fact
                        {
                            name = "Type",
                            value = getSource(application),
                        },
                    ],
                },
            };

            if (request != default)
            {
                sections = sections
                    .Append(
                        new MessageCard.Section
                        {
                            title = "REQUEST",
                            markdown = false, // so that underscores are not stripped
                            facts =
                                [
                                    new MessageCard.Section.Fact
                                    {
                                        name = "Http Method",
                                        value = request.Method.Method,
                                    },
                                    new MessageCard.Section.Fact
                                    {
                                        name = "URL",
                                        value = request.GetAbsoluteUri().OriginalString,
                                    },
                                ]
                        })
                    .ToArray();

                if (request.Headers.Any())
                    sections = sections
                        .Append(
                            new MessageCard.Section
                            {
                                title = "REQUEST HEADERS",
                                markdown = false, // so that underscores are not stripped
                                facts = request.Headers
                                    .Where(x => !x.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                                    .Select(
                                        header => new MessageCard.Section.Fact
                                        {
                                            name = $"{header.Key}:",
                                            value = header.Value.Join(","),
                                        })
                                    .ToArray(),
                            })
                        .ToArray();

                // put authorization header in own section for more compact viewing on mobile
                var authorizations = request.Headers
                    .Where(x => x.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (authorizations.Length > 0)
                    sections = sections
                        .Append(
                            new MessageCard.Section
                            {
                                title = "AUTHORIZATION",
                                markdown = false,
                                text = authorizations[0].Value.Join(","),
                            })
                        .ToArray();

                var content = await ReadContentAsync();
                if (content.HasBlackSpace())
                    sections = sections
                        .Append(
                            new MessageCard.Section
                            {
                                title = "CONTENT",
                                text = $"<blockquote>{content}</blockquote>",
                            })
                        .ToArray();

                if (request.Properties.ContainsKey(HttpApplication.DiagnosticsLogProperty))
                {
                    var diagnosticsLogs = (string[])request.Properties[HttpApplication.DiagnosticsLogProperty];
                    sections = sections
                        .Append(
                            new MessageCard.Section
                            {
                                title = "LOG",
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
                                title = "DIAGNOSTICS",
                                text = $"<blockquote>{log}</blockquote>",
                            })
                        .ToArray();
                }
            }
            
            if (getAdditionalInformation != default)
                sections = sections
                    .Append(getAdditionalInformation())
                    .ToArray();

            if (response != default)
            {
                var responseParam = response.Headers
                .Where(hdr => hdr.Key == Middleware.HeaderStatusName)
                .Where(hdr => hdr.Value.AnyNullSafe())
                .First(
                    (hdr, next) => hdr.Value.First(),
                    () => "");

                sections = sections
                    .Append(
                        new MessageCard.Section
                        {
                            title = "RESPONSE",
                            markdown = false, // so that underscores are not stripped
                            facts = new MessageCard.Section.Fact[]
                            {
                                new MessageCard.Section.Fact
                                {
                                    name = "Response Param",
                                    value = responseParam,
                                },
                                new MessageCard.Section.Fact
                                {
                                    name = "Status Code",
                                    value = $"{(int)response.StatusCode} ({response.StatusCode})",
                                },
                                new MessageCard.Section.Fact
                                {
                                    name = "Reason Phrase",
                                    value = response.ReasonPhrase,
                                },
                            },
                        })
                    .ToArray();
            }

            var actions = new MessageCard.ActionCard[] { };
            if (monitoringRequest != null)
            {
                sections = sections
                    .Append(
                        new MessageCard.Section
                        {
                            title = "MONITORING REQUEST",
                            text = monitoringRequest.id.ToString(),
                        })
                    .ToArray();

                if (request != default)
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
            }

            if (ex != default)
            {
                sections = sections
                    .Append(
                        new MessageCard.Section
                        {
                            title = "EXCEPTION",
                            markdown = false,
                            facts = new MessageCard.Section.Fact[]
                            {
                                new MessageCard.Section.Fact
                                {
                                    name = "Message",
                                    value = ex.Message,
                                },
                                new MessageCard.Section.Fact
                                {
                                    name = "Type",
                                    value = ex.GetType().FullName,
                                },
                                new MessageCard.Section.Fact
                                {
                                    name = "Stack Trace",
                                    value = (ex.StackTrace ?? string.Empty).Replace("\n", "<br />"),
                                },
                            },
                        })
                    .ToArray();                
            }

            var message = new MessageCard
            {
                summary = appName,
                themeColor = "F00807",
                sections = sections,
                potentialAction = actions,
            };
            return message;

            string getSource(IApplication application)
            {
                return application == default || application is IFunctionApplication
                    ? "Azure Functions"
                    : "Web API";
            }

            string getServerDescription()
            {
                var description = System.Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
                if (description.IsNullOrWhiteSpace())
                    description = System.Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
                if (description.IsNullOrWhiteSpace())
                    description = System.Environment.GetEnvironmentVariable(EastFive.Api.AppSettings.SiteUrl);
                if (description.IsNullOrWhiteSpace())
                    description = System.Environment.MachineName;
                if (description.IsNullOrWhiteSpace())
                    description = "host not identified";  
                return description;
            }

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
