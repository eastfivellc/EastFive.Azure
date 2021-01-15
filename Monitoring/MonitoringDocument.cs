using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using BlackBarLabs.Persistence.Azure;
using BlackBarLabs.Persistence.Azure.Attributes;
using BlackBarLabs.Persistence.Azure.StorageTables;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Web.Configuration;
using Microsoft.Azure.Cosmos.Table;
using Newtonsoft.Json;
using EastFive.Extensions;
using EastFive.Reflection;
using BlackBarLabs.Extensions;
using EastFive.Linq.Expressions;
using System.Linq;
using EastFive.Azure.Persistence.AzureStorageTables;

namespace EastFive.Api.Azure.Monitoring
{
    [Serializable]
    [DataContract]
    [StorageResource(typeof(TwoThousandEighteenYearMonthGenerator))]
    [StorageTable]
    public class MonitoringDocument : IReferenceable
    {
        [JsonIgnore]
        public Guid id => monitoringDocumentRef.id;

        [RowKey]
        public IRef<MonitoringDocument> monitoringDocumentRef;

        [ETag]
        [JsonIgnore]
        public string eTag;

        [Storage]
        public Guid AuthenticationId { get; set; }

        [TwoThousandEighteenYearMonth]
        [Storage]
        public DateTime Time { get; set; }

        [Storage]
        public string Method { get; set; }

        [Storage]
        public string Controller { get; set; }

        [Storage]
        public string Content { get; set; }

        public static Task<TResult> CreateAsync<TResult>(Guid id, 
                Guid authenticationId, DateTime time, string method, string controller, string content,
            Func<TResult> onSuccess)
        {
            var doc = new MonitoringDocument();
            doc.monitoringDocumentRef = id.AsRef<MonitoringDocument>();
            doc.AuthenticationId = authenticationId;
            doc.Time = time;
            doc.Method = method;
            doc.Controller = controller;
            doc.Content = content;

            return doc.StorageCreateAsync(
                (discard) => onSuccess(),
                () => throw new Exception("Guid not unique"));
        }

        public class TwoThousandEighteenYearMonthAttribute : Attribute,
            IModifyAzureStorageTablePartitionKey, IComputeAzureStorageTablePartitionKey
        {
            public string GeneratePartitionKey(string rowKey, object value, MemberInfo memberInfo)
            {
                return ComputePartitionKey(rowKey, value, memberInfo);
            }

            public static string ComputePartitionKey(string rowKey, object value, MemberInfo memberInfo)
            {
                var dateTimeValueObj = memberInfo.GetValue(value);
                return ComputePartitionKey(dateTimeValueObj);
            }

            public EntityType ParsePartitionKey<EntityType>(EntityType entity, string yearMonthString, MemberInfo memberInfo)
            {
                // Missing day value so can't reverse it
                //if (memberInfo.GetPropertyOrFieldType().IsAssignableFrom(typeof(DateTime)))
                //{
                //    if (yearMonthString.TryMatchRegex(
                //            "(?<year>[0-9][0-9][0-9][0-9])(?<month>[0-9][0-9])",
                //        (year, month) => new { year, month },
                //        out (int, int) yearMonth))
                //    {
                //        int month = int.Parse(yearMonth.month);
                //        int year = int.Parse(yearMonth.year);
                //        var date = new DateTime(year, 1, 1).AddDays(dayOfYear - 1);
                //        return date;

                //    }
                //    memberInfo.SetValue(ref entity, partitionDate);
                //}

                // otherwise, discard ...?
                return entity;
            }

            public string ComputePartitionKey(object memberValue, MemberInfo memberInfo, string rowKey,
                params KeyValuePair<MemberInfo, object>[] extraValues)
            {
                return ComputePartitionKey(memberValue);
            }

            public IEnumerable<string> GeneratePartitionKeys(Type type, int skip, int top)
            {
                var epochStart = new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                int daysThroughPresent = (int)((DateTime.UtcNow.Date - epochStart).TotalDays + 1);
                return Enumerable.Range(0, daysThroughPresent)
                    .Select(offset => epochStart.AddDays(offset))
                    .Select(date => date.ToString("yyyyMM"))
                    .Distinct();
            }

            public static string ComputePartitionKey(object dateTimeValueObj)
            {
                if (dateTimeValueObj.IsDefaultOrNull())
                    return "201801";
                if (dateTimeValueObj.GetType().IsNullable())
                {
                    if (!dateTimeValueObj.NullableHasValue())
                        return "201801";
                    dateTimeValueObj = dateTimeValueObj.GetNullableValue();
                }
                var dateTimeValue = (DateTime)dateTimeValueObj;

                return dateTimeValue.ToString("yyyy") + dateTimeValue.ToString("MM");
            }
        }
    }
}
