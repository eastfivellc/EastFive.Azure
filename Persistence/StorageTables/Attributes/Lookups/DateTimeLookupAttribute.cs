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
using EastFive.Azure.Persistence;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class DateTimeLookupAttribute : StorageLookupAttribute,
        IModifyAzureStorageTableSave, IProvideFindBy
    {
        public const double seconds = 1.0;
        public const double minutes = 60.0;
        public const double minutesPerHour = 60;
        public const double hours = minutes * minutesPerHour;
        public const double hoursPerDay = 24;
        public const double days = hours * hoursPerDay;

        /// <summary>
        /// Total seconds
        /// </summary>
        public double Row { get; set; }

        /// <summary>
        /// Total seconds
        /// </summary>
        public double Partition { get; set; }

        private bool ignoreDefault = true;
        public bool IgnoreDefault
        {
            get => ignoreDefault;
            set
            {
                ignoreDefault = value;
            }
        }

        public override IEnumerable<IRefAst> GetLookupKeys(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues)
        {
            if (lookupValues.Count() != 1)
                throw new ArgumentException();

            var lookupMemberValueKvp = lookupValues.Single();
            var lookupMemberInfo = lookupMemberValueKvp.Key;

            DateTime? GetDateTime()
            {
                if (typeof(DateTime?).IsAssignableFrom(lookupMemberInfo.GetMemberType()))
                    return (DateTime?)lookupMemberValueKvp.Value;

                if (typeof(DateTime).IsAssignableFrom(lookupMemberInfo.GetMemberType()))
                    return (DateTime)lookupMemberValueKvp.Value;

                if (typeof(DateTimeOffset?).IsAssignableFrom(lookupMemberInfo.GetMemberType()))
                {
                    var dtoMaybe = (DateTimeOffset?)lookupMemberValueKvp.Value;
                    if (!dtoMaybe.HasValue)
                        return default(DateTime?);
                    var dto = dtoMaybe.Value;
                    return dto.DateTime;
                }

                if (typeof(DateTimeOffset).IsAssignableFrom(lookupMemberInfo.GetMemberType()))
                {
                    var dto = (DateTimeOffset)lookupMemberValueKvp.Value;
                    return dto.DateTime;
                }

                throw new ArgumentException();
            }
            var lookupValueMaybe = GetDateTime();
            if (!lookupValueMaybe.HasValue)
                return Enumerable.Empty<IRefAst>();
            var lookupValue = lookupValueMaybe.Value;
            if(ignoreDefault && lookupValue.IsDefault())
                return Enumerable.Empty<IRefAst>();

            var lookupRowKey = ComputeLookupKey(lookupValue, TimeSpan.FromSeconds(this.Row));
            var lookupPartitionKey = ComputeLookupKey(lookupValue, TimeSpan.FromSeconds(this.Partition));

            return lookupRowKey.AsAstRef(lookupPartitionKey).AsEnumerable();

            
        }

        internal static string ComputeLookupKey(DateTime memberValue, TimeSpan timeSpan)
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

        protected override PropertyLookupInformation GetInfo(StorageLookupTable slt)
        {
            var info = base.GetInfo(slt);
            var value = (string)info.value;
            var timeSpan = TimeSpan.FromSeconds(this.Row);
            info.value = GetValue();
            return info;

            object GetValue()
            {
                var yearStr = value.Substring(0, 4);
                if (!int.TryParse(yearStr, out int year))
                    return value;
                if (timeSpan.TotalDays >= 28)
                    return new DateTime(year, 0, 0);

                var monthStr = value.Substring(4, 2);
                if (!int.TryParse(monthStr, out int month))
                    return new DateTime(year, 0, 0);
                if (timeSpan.TotalDays >= 1.0)
                    return new DateTime(year, month, 0);

                var dayStr = value.Substring(6, 2);
                if (!int.TryParse(dayStr, out int day))
                    return new DateTime(year, month, 0);
                if (timeSpan.TotalHours >= 1.0)
                    return new DateTime(year, month, day);

                var hourStr = value.Substring(8, 2);
                if (!int.TryParse(hourStr, out int hour))
                    return new DateTime(year, month, day);
                if (timeSpan.TotalMinutes >= 1.0)
                    return new DateTime(year, month, day, hour, 0, 0);

                var minStr = value.Substring(10, 2);
                if (!int.TryParse(minStr, out int min))
                    return new DateTime(year, month, day, hour, 0, 0);
                if (timeSpan.Seconds >= 1.0)
                    return new DateTime(year, month, day, hour, min, 0);

                var secondsStr = value.Substring(12, 2);
                if (!int.TryParse(secondsStr, out int seconds))
                    return new DateTime(year, month, day, hour, min, 0);
                return new DateTime(year, month, day, hour, min, seconds);
            }
        }
    }
}
