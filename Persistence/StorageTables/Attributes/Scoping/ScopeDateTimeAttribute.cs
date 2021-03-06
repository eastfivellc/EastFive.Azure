﻿using EastFive.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public enum TimeSpanUnits
    {
        minutes,
        hours,
        days,
        weeks,
        months,
        years,
    }

    public class ScopeDateTimeAttribute : Attribute, IScopeKeys
    {
        public string Scope { get; private set; }

        public double Order { get; set; } = 0.0;

        public int SpanLength { get; set; } = 1;

        public TimeSpanUnits SpanUnits { get; set; } = TimeSpanUnits.days;

        public double OffsetHours { get; set; } = default;

        /// <summary>
        /// If the SpanUnits are weeks, this is the day of the first week.
        /// </summary>
        public DateTime WeeksEpoch { get; set; } = new DateTime(2017, 1, 1);

        public string MutateKey(string currentKey, MemberInfo key, object value, out bool ignore)
        {
            ignore = false;
            var dateTime = (DateTime)value;
            if (!OffsetHours.IsDefault())
                dateTime = dateTime + TimeSpan.FromHours(OffsetHours);
            var dtPartition = ComputeLookupKey(dateTime, SpanLength, SpanUnits, WeeksEpoch);
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

    public class ScopeDateTime1Attribute : ScopeDateTimeAttribute
    {
        public ScopeDateTime1Attribute(string scope) : base(scope)
        {
        }
    }
}
