using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Linq.Expressions;
using EastFive.Collections.Generic;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence.Azure.StorageTables.Driver;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class DateTimeLookupAttribute : StorageLookupAttribute,
        IModifyAzureStorageTableSave, IProvideFindBy
    {
        /// <summary>
        /// Total seconds
        /// </summary>
        public double Row { get; set; }

        /// <summary>
        /// Total seconds
        /// </summary>
        public double Partition { get; set; }

        public override IEnumerable<MemberInfo> ProvideLookupMembers(MemberInfo decoratedMember)
        {
            return decoratedMember.AsEnumerable();
        }

        public override IEnumerable<IRefAst> GetLookupKeys(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues)
        {
            if (lookupValues.Count() != 1)
                throw new ArgumentException();

            var lookupMemberValueKvp = lookupValues.Single();
            var lookupMemberInfo = lookupMemberValueKvp.Key;
            if (!typeof(DateTime).IsAssignableFrom(lookupMemberInfo.GetMemberType()))
                throw new ArgumentException();

            var lookupValue = (DateTime)lookupMemberValueKvp.Value;
            var lookupRowKey = ComputeLookupKey(lookupValue, TimeSpan.FromSeconds(this.Row));
            var lookupPartitionKey = ComputeLookupKey(lookupValue, TimeSpan.FromSeconds(this.Partition));

            return lookupRowKey.AsAstRef(lookupPartitionKey).AsEnumerable();

            string ComputeLookupKey(DateTime memberValue, TimeSpan timeSpan)
            {
                var key = $"{memberValue.Year}";
                if (timeSpan.TotalDays >= 28)
                    return key;
                key = $"{key}{memberValue.Month.ToString("D2")}";
                if (timeSpan.TotalDays >= 1.0)
                    return key;
                key = $"{key}{memberValue.Day.ToString("D2")}";
                if (timeSpan.TotalHours >= 1.0)
                    return key;
                key = $"{key}{memberValue.Hour.ToString("D2")}";
                if (timeSpan.TotalMinutes >= 60.0)
                    return key;
                key = $"{key}{memberValue.Minute.ToString("D2")}";
                if (timeSpan.Seconds >= 60.0)
                    return key;
                return $"{key}{memberValue.Second.ToString("D2")}";
            }
        }
    }
}
