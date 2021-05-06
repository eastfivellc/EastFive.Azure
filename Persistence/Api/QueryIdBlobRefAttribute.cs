using EastFive.Api;
using EastFive.Persistence.Azure.StorageTables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Persistence
{
    public class QueryIdBlobRefAttribute : QueryParameterAttribute
    {
        /// <summary>The property name set by a <see cref="ApiPropertyAttribute"/>'s 
        ///     <code>PropertyName</code> attribute for the IBlobRef property.
        /// </summary>
        /// <remarks>
        /// Must set one of <para name="PropertyName">PropertyName</para> or
        /// <param name="ContainerName"></param> so the appropriate container can be resolved.
        /// </remarks>
        public string PropertyName { get; set; }

        /// <summary>Directly specifies the container name for the IBlobRef.</summary>
        /// <remarks>
        /// Must set one of <para name="PropertyName">PropertyName</para> or
        /// <param name="ContainerName"></param> so the appropriate container can be resolved.
        /// </remarks>
        public string ContainerName { get; set; }

        public override string Name
        {
            get
            {
                var name = base.Name;
                if (name.HasBlackSpace())
                    return name;
                return "id";
            }
            set => base.Name = value;
        }

        private class BlobRef : IBlobRef
        {
            public string Id { get; private set; }

            public string ContainerName { get; private set; }
        }
    }
}
