using EastFive.Extensions;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    //public enum TimeSpanUnits
    //{
    //    minutes,
    //    hours,
    //    days,
    //    weeks,
    //    months,
    //    years,
    //}

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public class ScopeDateTimeAttribute : Attribute, IScopeKeys
    {
        public string Scope { get; private set; }

        public double Order { get; set; } = 0.0;

        public int SpanLength { get; set; } = 1;

        public TimeSpanUnits SpanUnits { get; set; } = TimeSpanUnits.days;

        public double OffsetHours { get; set; } = default;

        public bool IgnoreNull { get; set; } = false;

        /// <summary>
        /// If the SpanUnits are weeks, this is the day of the first week.
        /// </summary>
        public string WeeksEpoch
        {
            get
            {
                return this.weeksEpoch.ToShortDateString();
            }
            set
            {
                DateTime.TryParse(value, out var dt);
                weeksEpoch = dt;
            }
        }


        public DateTime weeksEpoch = new DateTime(2017, 1, 1);

        public string MutateKey(string currentKey, MemberInfo key, object value, out bool ignore)
        {
            if(key.GetMemberType().IsNullable())
            {
                if (!value.NullableHasValue())
                {
                    ignore = IgnoreNull;
                    return currentKey;
                }
                value = value.GetNullableValue();
            }

            var dateTime = (DateTime)value;
            if (dateTime.IsDefault())
            {
                ignore = true;
                return currentKey;
            }

            ignore = false;
            if (!OffsetHours.IsDefault())
                dateTime = dateTime + TimeSpan.FromHours(OffsetHours);
            var dtPartition = ComputeLookupKey(dateTime, SpanLength, SpanUnits, weeksEpoch);
            return $"{currentKey}{dtPartition}";
        }

        public ScopeDateTimeAttribute(string scope)
        {
            this.Scope = scope;
        }

        internal static string ComputeLookupKey(DateTime memberValue, int spanLength, TimeSpanUnits spanUnits,
            DateTime? weeksEpochMaybe = default)
        {
            if (spanUnits == TimeSpanUnits.weeks)
            {
                var weeksEpoch = weeksEpochMaybe ?? new DateTime(2017, 1, 1);
                var weeks = (int)((memberValue - weeksEpoch).TotalDays / 7);
                return $"{weeks}";
            }
            var key = $"{memberValue.Year}";
            if (spanUnits == TimeSpanUnits.years)
                return key;
            key = $"{key}{memberValue.Month.ToString("D2")}";
            if (spanUnits == TimeSpanUnits.months)
                return key;
            key = $"{key}{memberValue.Day.ToString("D2")}";
            if (spanUnits == TimeSpanUnits.days)
                return key;
            key = $"{key}{memberValue.Hour.ToString("D2")}";
            if (spanUnits == TimeSpanUnits.hours)
                return key;
            key = $"{key}{memberValue.Minute.ToString("D2")}";
            if (spanUnits == TimeSpanUnits.minutes)
                return key;
            return $"{key}{memberValue.Second.ToString("D2")}";
        }

    }
}
