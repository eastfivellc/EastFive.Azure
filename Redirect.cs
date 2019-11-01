﻿using BlackBarLabs.Api;
using EastFive;
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
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Routing;
using System.ComponentModel;
using EastFive.Api.Azure;

namespace EastFive.Azure
{
    [FunctionViewController6(
         ContentType = "application/x-redirect",
         Resource = typeof(Redirect),
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
        public static HttpResponseMessage QueryByResourceIdAndTypeAsync(
                [QueryParameter(Name = ResourceTypePropertyName)]Type resourceType,
                [QueryParameter(Name = ResourcePropertyName)]Guid resourceId,
                AzureApplication application,
            RedirectResponse onRedirect,
            UnauthorizedResponse onUnauthorized,
            NotFoundResponse onRedirectNotFound,
            ConfigurationFailureResponse onConfigurationFailure)
        {
            return EastFive.Web.Configuration.Settings.GetUri(
                EastFive.Azure.AppSettings.SpaSiteLocation,
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
