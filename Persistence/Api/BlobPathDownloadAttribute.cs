using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Web;

using EastFive.Api;
using EastFive.Azure.Functions;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Reflection;

namespace EastFive.Azure.Persistence
{
	public abstract class BlobPathDownloadAttribute : SecureLinkAttribute, ICastJsonProperty
    {
        public string Container { get; set; }

        public string ContainerPropertyName { get; set; }

        public BlobPathDownloadAttribute(string method) : base(method)
        {
        }

        protected override Uri GetUrl(MemberInfo documentsMember, ParameterInfo paramInfo, IProvideApiValue apiValueProvider,
            object objectValue, object memberValue, HttpActionAttribute httpActionAttr, IHttpRequest httpRequest, IApplication application)
        {
            var path = (string)memberValue;

            var containerValue = documentsMember.DeclaringType
                .GetPropertyOrFieldMembers()
                .Where(prop => String.Equals(prop.Name, this.ContainerPropertyName, StringComparison.Ordinal))
                .First(
                    (prop, next) => (string)prop.GetValue(objectValue),
                    () =>
                    {
                        return this.Container;
                    });

            if (containerValue.IsNullOrWhiteSpace())
            {
                return default;
            }

            var url = GetUrl(httpRequest, httpActionAttr, containerValue, path);
            return url;
        }

        protected abstract Uri GetUrl(IHttpRequest httpRequest,
                HttpActionAttribute httpActionAttr, string containerValue, string path);
    }
}

