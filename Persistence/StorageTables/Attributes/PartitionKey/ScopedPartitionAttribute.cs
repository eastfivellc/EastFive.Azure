using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using static EastFive.Persistence.Azure.StorageTables.PartitionKeyAttribute;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class ScopedPartitionAttribute : ScopedKeyAttribute,
        IModifyAzureStorageTablePartitionKey, IComputeAzureStorageTablePartitionKey,
        IGenerateAzureStorageTablePartitionIndex
    {
        protected override string KeyScoping => Scoping;

        protected override string KeyName => "PartitionKey";

        public const string Scoping = "__PartitionKeyScoping__";

        public string GeneratePartitionKey(string rowKey, object value, MemberInfo decoratedMember)
        {
            return base.GenerateKey(value, decoratedMember);
        }

        public EntityType ParsePartitionKey<EntityType>(EntityType entity, string value, MemberInfo memberInfo)
        {
            return base.ParseKey(entity, value, memberInfo);
        }

        public string ComputePartitionKey(object refKey, MemberInfo decoratedMember, string rowKey,
            params KeyValuePair<MemberInfo, object>[] lookupValues)
        {
            return base.ComputeKey(decoratedMember, lookupValues);
        }

        public string GeneratePartitionIndex(MemberInfo member, string rowKey)
        {
            return GenerateIndex(member);
        }
    }

}
