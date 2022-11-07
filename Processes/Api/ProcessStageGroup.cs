using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Net.Http;

using Microsoft.AspNetCore.Mvc.Routing;

using Newtonsoft.Json;

using EastFive.Api.Resources;
using EastFive.Azure;
using EastFive.Api.Controllers;
using EastFive.Linq;
using EastFive.Extensions;

namespace EastFive.Api.Azure.Resources
{
    [DataContract]
    [FunctionViewController(Route = "ProcessStageGroup")]
    public class ProcessStageGroup
    {
        public const string IdPropertyName = "id";
        [JsonProperty(PropertyName = IdPropertyName)]
        [DataMember(Name = IdPropertyName)]
        public WebId Id { get; set; }

        public const string TitlePropertyName = "title";
        [JsonProperty(PropertyName = TitlePropertyName)]
        public string Title { get; set; }

        public const string RankPropertyName = "rank";
        [JsonProperty(PropertyName = RankPropertyName)]
        public double Rank { get; set; }


        internal static Resources.ProcessStageGroup[] stages = new[]
        {
            new Resources.ProcessStageGroup
            {
                Id = ProcessStagesGroups.group1Id,
                Rank = 1.0,
                Title = "Ordered",
            },
            new Resources.ProcessStageGroup
            {
                Id = ProcessStagesGroups.group2Id,
                Rank = 2.0,
                Title = "Confirmed",
            },
            //new ProcessStageGroup
            //{
            //    Id = Guid.Parse("4b879bad-6543-4944-9a97-642661090176"),
            //    Rank = 3.0,
            //    Title = "Complete",
            //},
        };

        #region GET

        [EastFive.Api.HttpGet]
        public static Task<IHttpResponse> FindByIdAsync(
                [QueryParameter(CheckFileName = true)]Guid id,
                EastFive.Api.Security security,
            ContentResponse onFound,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return stages.First(
                async (stage, next) =>
                {
                    if (stage.Id.UUID == id)
                        return onFound(stage);
                    return await next();
                },
                () => onNotFound().AsTask());

            //return Connectors.FindByIdAsync(id,
            //        security.performingAsActorId, security.claims,
            //    (synchronization, destinationIntegrationId) => onFound(GetResource(synchronization, destinationIntegrationId, url)),
            //    () => onNotFound(),
            //    () => onUnauthorized());
        }

        [EastFive.Api.HttpGet]
        public static IHttpResponse FindAll(
                EastFive.Api.Security security,
            MultipartAcceptArrayResponse onMultipart)
        {
            return onMultipart(stages);
        }

        #endregion


        [EastFive.Api.HttpOptions(MatchAllBodyParameters = false)]
        public static IHttpResponse Options(
            ContentResponse onOption)
        {
            return onOption(stages[1]);
        }
    }
}