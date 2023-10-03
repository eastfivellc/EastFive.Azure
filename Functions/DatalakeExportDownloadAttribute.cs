using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Web;

using EastFive.Api;
using EastFive.Azure.Functions;
using EastFive.Azure.Persistence;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Reflection;

using Newtonsoft.Json;

namespace EastFive.Azure.Functions
{
	public abstract class DatalakeExportDownloadAttribute : SecureLinkAttribute, ICastJsonProperty
    {
        public DatalakeExportDownloadAttribute(string method) : base(method)
        {
        }

        protected override Uri GetUrl(MemberInfo documentsMember, ParameterInfo paramInfo, IProvideApiValue apiValueProvider,
            object objectValue, object memberValue, HttpActionAttribute httpActionAttr, IHttpRequest httpRequest, IApplication application)
        {
            return documentsMember.DeclaringType
                .GetPropertyOrFieldMembers()
                .Where(prop => String.Equals(prop.Name, nameof(IExportFromDatalake.id), StringComparison.OrdinalIgnoreCase))
                .First(
                    (prop, next) =>
                    {
                        var id = (Guid)prop.GetValue(objectValue);
                        var dlExportRef = id.AsRef<IExportFromDatalake>();
                        var url = GetUrl(httpRequest, httpActionAttr, dlExportRef);
                        return url;
                    },
                    () =>
                    {
                        return default(Uri);
                    });
        }

        protected abstract Uri GetUrl(IHttpRequest httpRequest,
                HttpActionAttribute httpActionAttr, IRef<IExportFromDatalake> dlExportRef);
    }
}

