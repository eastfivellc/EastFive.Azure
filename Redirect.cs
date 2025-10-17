using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

using Newtonsoft.Json;

using Microsoft.AspNetCore.Mvc.Routing;

using EastFive;
using EastFive.Api.Azure;
using EastFive.Web.Configuration;
using EastFive.Collections.Generic;
using EastFive.Api;
using EastFive.Api.Controllers;
using EastFive.Async;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

namespace EastFive.Azure
{
    [FunctionViewController(
         ContentType = "application/x-redirect",
         Route = "redirect")]
    [StorageTable]
    public struct Redirect : IReferenceable
    {
        #region Properties

        [JsonIgnore]
        public Guid id => this.redirectRef.id;

        public const string IdPropertyName = "id";
        [JsonProperty(PropertyName = IdPropertyName)]
        [ApiProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [StandardParititionKey]
        public IRef<Redirect> redirectRef;

        public const string ResourcePropertyName = "resource";
        [JsonProperty(PropertyName = ResourcePropertyName)]
        [ApiProperty(PropertyName = ResourcePropertyName)]
        [Storage]
        public IRef<IReferenceable> resource;

        public const string ResourceTypePropertyName = "type";
        [JsonProperty(PropertyName = ResourceTypePropertyName)]
        [ApiProperty(PropertyName = ResourceTypePropertyName)]
        [Storage]
        public Type resourceType;

        #endregion

        #region HTTP Actions

        [HttpGet]
        [Description("Looks for the redirect by the resource ID and resource type.")]
        public static IHttpResponse QueryByResourceIdAndTypeAsync(
                [QueryParameter(Name = ResourceTypePropertyName)]Type resourceType,
                [QueryParameter(Name = ResourcePropertyName)]Guid resourceId,
                IApiApplication application,
            RedirectResponse onRedirect,
            UnauthorizedResponse onUnauthorized,
            NotFoundResponse onRedirectNotFound,
            ConfigurationFailureResponse onConfigurationFailure)
        {
            return AppSettings.SPA.SiteLocation.ConfigurationUri(
                siteUrl =>
                {
                    var resourceName = application.GetResourceMime(resourceType);
                    var redirectUrl = siteUrl
                        .AppendToPath("redirect")
                        .AddQueryParameter("type", resourceName)
                        .AddQueryParameter("id", resourceId.ToString());
                    return onRedirect(redirectUrl);
                },
                (why) => onConfigurationFailure(why, ""));
        }

        #endregion
    }
}
