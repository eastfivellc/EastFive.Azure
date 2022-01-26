using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Azure.StorageTables.Driver;
using EastFive.Collections.Generic;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Serialization;
using EastFive.Reflection;


namespace EastFive.Azure.Persistence.Blobs
{
    public interface IDefineBlobContainer
    {
        string ContainerName { get; }
    }

    public class BlobContainerAttribute 
        : System.Attribute, IDefineBlobContainer
    {
        public string ContainerName { get; set; }
    }

    public interface IMigrateBlobIdAttribute
    {
        string IdName { get; }
    }

    public class MigrateBlobIdAttribute : Attribute, IMigrateBlobIdAttribute
    {
        public string IdName { get; set; }
    }

    public class BlobContainerRefAttribute
        : System.Attribute, IDefineBlobContainer
    {
        private Type type;
        private string propertyName;

        public string ContainerName
        {
            get
            {
                return type
                    .GetPropertyOrFieldMembers()
                    .Where(memberInfo => memberInfo.Name.Equals(propertyName, StringComparison.Ordinal))
                    .First()
                    .BlobContainerName();
            }
        }

        public BlobContainerRefAttribute(Type type, string propertyName)
        {
            this.type = type;
            this.propertyName = propertyName;
        }
    }
}
