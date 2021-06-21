using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Persistence.Azure.StorageTables;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Persistence
{
    internal interface IApiBoundBlobRef : IBlobRef
    {
        string ContainerName { get; set; }
    }

    public class BlobRefBindingAttribute : Attribute, IBindApiParameter<string>
        IBindApiParameter<Microsoft.AspNetCore.Http.IFormFile>
    {
        public TResult Bind<TResult>(Type type, string content,
                IApplication application,
            Func<object, TResult> onParsed,
            Func<string, TResult> onDidNotBind,
            Func<string, TResult> onBindingFailure)
        {
            if (!typeof(IBlobRef).IsAssignableFrom(type))
                return onDidNotBind("BlobRefBindingAttribute only binds IBlobRef");

            var value = new BlobRefString(content);
            return onParsed(value);
        }

        private class BlobRefString : IApiBoundBlobRef
        {
            public string Id { get; private set; }

            public string ContainerName { get; set; }

            public BlobRefString(string id)
            {
                Id = id;
            }

            public Task SaveAsync() => throw new NotImplementedException();
        }

        public TResult Bind<TResult>(Type type, IFormFile content,
                IApplication application,
            Func<object, TResult> onParsed,
            Func<string, TResult> onDidNotBind,
            Func<string, TResult> onBindingFailure)
        {
            if (!typeof(IBlobRef).IsAssignableFrom(type))
                return onDidNotBind("BlobRefBindingAttribute only binds IBlobRef");

            var value = new BlobRefFormFile(content);
            return onParsed(value);
        }

        private class BlobRefFormFile : IApiBoundBlobRef
        {
            public string Id { get; private set; }

            public string ContainerName { get; set; }

            public IFormFile content;

            public BlobRefFormFile(IFormFile content)
            {
                Id = Guid.NewGuid().ToString("N");
            }

            public Task SaveAsync()
            {
                return content.OpenReadStream().BlobCreateAsync(
                    Id, this.ContainerName,
                    () => true);
            }
        }
    }
}
