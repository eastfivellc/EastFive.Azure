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
    public class ScopedRowKeyAttribute : ScopedKeyAttribute,
        IModifyAzureStorageTableRowKey, IComputeAzureStorageTableRowKey
    {
        protected override string KeyScoping => Scoping;

        protected override string KeyName => "RowKey";

        public const string Scoping = "__RowKeyScoping__";

        public string GenerateRowKey(object value, MemberInfo decoratedMember)
        {
            return base.GenerateKey(value, decoratedMember);
        }

        public EntityType ParseRowKey<EntityType>(EntityType entity, string value, MemberInfo decoratedMember)
        {
            return base.ParseKey(entity, value, decoratedMember);
        }

        public string ComputeRowKey(object memberValue, MemberInfo decoratedMember,
            params KeyValuePair<MemberInfo, object>[] extraValues)
        {
            return base.ComputeKey(decoratedMember,
                extraValues
                    .Append(decoratedMember.PairWithValue(memberValue))
                    .ToArray());
        }
    }

}
