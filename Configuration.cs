using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Security.Claims;

using Microsoft.AspNetCore.Mvc.Routing;

using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Api.Controllers;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Linq.Expressions;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Web;
using EastFive.Web.Configuration;
using Newtonsoft.Json;

namespace EastFive.Azure.Configuration
{
    [DataContract]
    [FunctionViewController6(
        Route = "Configuration",
        Resource = typeof(Configuration),
        ContentType = "x-application/auth-configuration",
        ContentTypeVersion = "0.1")]
    public struct Configuration
    {
        #region Properties

        public const string LocationPropertyName = "location";
        /// <summary>
        /// Where to find this value
        /// </summary>
        [ApiProperty(PropertyName = LocationPropertyName)]
        [JsonProperty(PropertyName = LocationPropertyName)]
        public string location;

        public const string DescriptionPropertyName = "description";
        /// <summary>
        /// What is provided by this value
        /// </summary>
        [ApiProperty(PropertyName = DescriptionPropertyName)]
        [JsonProperty(PropertyName = DescriptionPropertyName)]
        public string description;

        public const string MoreInfoPropertyName = "more_info";
        /// <summary>
        /// More information about the key.
        /// </summary>
        [ApiProperty(PropertyName = MoreInfoPropertyName)]
        [JsonProperty(PropertyName = MoreInfoPropertyName)]
        public string moreInfo;

        public const string DeploymentOverridePropertyName = "deployment_override";
        /// <summary>
        /// Should the value provided in the (web|app).config be overwritten in a deployed environment.
        /// </summary>
        [ApiProperty(PropertyName = DeploymentOverridePropertyName)]
        [JsonProperty(PropertyName = DeploymentOverridePropertyName)]
        public DeploymentOverrides deploymentOverride;

        public const string DeploymentSecurityConcernPropertyName = "deployment_security_concern";
        /// <summary>
        /// Not overriding this value in a deployment is a security concern
        /// </summary>
        [ApiProperty(PropertyName = DeploymentSecurityConcernPropertyName)]
        [JsonProperty(PropertyName = DeploymentSecurityConcernPropertyName)]
        public bool deploymentSecurityConcern;

        public const string PrivateRepositoryOnlyPropertyName = "private_repository_only";
        /// <summary>
        /// This value should not be stored in a version control repository or anything other location
        /// that is publicly available.
        /// </summary>
        /// <remarks>
        /// These values are generally placed in a (App|Web).{Env}.config file that is flagged
        /// as ignored (i.e. .gitignore file) by the source control system.
        /// </remarks>
        [ApiProperty(PropertyName = PrivateRepositoryOnlyPropertyName)]
        [JsonProperty(PropertyName = PrivateRepositoryOnlyPropertyName)]
        public bool PrivateRepositoryOnly;

        public const string NamePropertyName = "name";
        [ApiProperty(PropertyName = NamePropertyName)]
        [JsonProperty(PropertyName = NamePropertyName)]
        [Storage]
        public string name;

        public const string TypePropertyName = "type";
        [ApiProperty(PropertyName = TypePropertyName)]
        [JsonProperty(PropertyName = TypePropertyName)]
        [Storage]
        public Type type;

        public const string ValuePropertyName = "value";
        [ApiProperty(PropertyName = ValuePropertyName)]
        [JsonProperty(PropertyName = ValuePropertyName)]
        [Storage]
        public string value;

        #endregion

        [Api.HttpGet]
        [RequiredClaim(ClaimTypes.Role, ClaimValues.Roles.SuperAdmin)]
        public static IHttpResponse GetAsync(
            IApplication application,
            ContentTypeResponse<Configuration[]> onFound)
        {
            var configs = application.ConfigurationTypes
                .SelectMany(configurationType =>
                    configurationType.Key.GetFields(BindingFlags.Public |
                            BindingFlags.Static | BindingFlags.FlattenHierarchy)
                        .Where(member => member.ContainsCustomAttribute<ConfigKeyAttribute>())
                        .Select(member => member.GetCustomAttribute<ConfigKeyAttribute>().PairWithKey(member)))
                .Select(
                    memberAttrKvp =>
                    {
                        var member = memberAttrKvp.Key;
                        var attr = memberAttrKvp.Value;
                        return new Configuration()
                        {
                            deploymentOverride = attr.DeploymentOverride,
                            deploymentSecurityConcern = attr.DeploymentSecurityConcern,
                            description = attr.Description,
                            location = attr.Location,
                            moreInfo = attr.MoreInfo,
                            name = $"{member.DeclaringType.FullName}.{member.Name}",
                            PrivateRepositoryOnly = attr.PrivateRepositoryOnly,
                            type = member.GetMemberType(),
                            value = (member.GetValue(null) as string).ConfigurationString(
                                v => v,
                                why => "Not defined"),
                        };
                    })
                .ToArray();
            return onFound(configs);
        }
    }

}