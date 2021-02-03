using EastFive;
using EastFive.Azure.Persistence.StorageTables.Backups;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EastFive.Azure.Persistence.Attributes
{
    public struct StringKey
    {
        public string Equal { get; set; }
        public string NotEqual { get; set; }
        public string GreaterThan { get; set; }
        public string GreaterThanOrEqual { get; set; }
        public string LessThan { get; set; }
        public string LessThanOrEqual { get; set; }
    }

    public interface StringKeyGenerator
    {
        IEnumerable<StringKey> GetKeys();
    }

    public class NoKeyGenerator : ListKeyGenerator
    {
        public NoKeyGenerator() : base(new string[] { }) { }
    }

    public class ListKeyGenerator : StringKeyGenerator
    {
        private readonly IEnumerable<string> items;

        public ListKeyGenerator(IEnumerable<string> items)
        {
            this.items = items;
        }

        public virtual IEnumerable<StringKey> GetKeys()
        {
            return items
                .NullToEmpty()
                .Select(
                    item => new StringKey
                    {
                        Equal = item,
                    });
        }
    }

    public class StandardPartitionKeyGenerator : RemainderKeyGenerator
    {
        public StandardPartitionKeyGenerator() : base(KeyExtensions.PartitionKeyRemainder - 1) { }
    }

    public class RemainderKeyGenerator : StringKeyGenerator
    {
        private readonly int positiveBound;

        public RemainderKeyGenerator(int positiveBound)
        {
            this.positiveBound = positiveBound;
        }

        public virtual IEnumerable<StringKey> GetKeys()
        {
            int negativeBound = -positiveBound;
            int count = (positiveBound * 2) + 1; // include zero value
            return Enumerable.Range(negativeBound, count)  // i.e. (-12, 25)
                .Select(
                    num => num.ToString())
                .Select(
                    partitionKey =>
                    {
                        return new StringKey
                        {
                            Equal = partitionKey,
                        };
                    });
        }
    }

    public class OnePlaceHexadecimalKeyGenerator : HexadecimalKeyRangeGenerator
    {
        public OnePlaceHexadecimalKeyGenerator() : base(1) { }
    }

    public class TwoPlaceHexadecimalKeyGenerator : HexadecimalKeyRangeGenerator
    {
        public TwoPlaceHexadecimalKeyGenerator() : base(2) { }
    }

    public class ThreePlaceHexadecimalKeyGenerator : HexadecimalKeyRangeGenerator
    {
        public ThreePlaceHexadecimalKeyGenerator() : base(3) { }
    }

    public class FourPlaceHexadecimalKeyGenerator : HexadecimalKeyRangeGenerator
    {
        public FourPlaceHexadecimalKeyGenerator() : base(4) { }
    }

    public class HexadecimalKeyRangeGenerator : StringKeyGenerator
    {
        private readonly string format;
        private readonly int count;

        public HexadecimalKeyRangeGenerator(int places)
        {
            this.format = $"x{places}";
            this.count = 0x1 << (places * 4);
        }

        public virtual IEnumerable<StringKey> GetKeys()
        {
            return Enumerable.Range(0, count)
            .Select(
                num =>
                {
                    var upper = num + 1;
                    var lowerRowKey = num.ToString(format);
                    var upperRowKey = upper.ToString(format);
                    if (upper < count)
                        return new StringKey
                        {
                            GreaterThanOrEqual = lowerRowKey,
                            LessThan = upperRowKey,
                        };
                    else
                        return new StringKey
                        {
                            GreaterThanOrEqual = lowerRowKey,
                        };
                });
        }
    }

    public class ConcatGenerator : StringKeyGenerator
    {
        private readonly StringKeyGenerator[] generators;
        public ConcatGenerator(params StringKeyGenerator[] generators)
        {
            this.generators = generators;
        }

        public IEnumerable<StringKey> GetKeys()
        {
            return generators.SelectMany(g => g.GetKeys());
        }
    }

    public class TwoThousandEighteenYearMonthGenerator : DayGenerator
    {
        public TwoThousandEighteenYearMonthGenerator() : base(new DateTime(2018,1,1,0,0,0,DateTimeKind.Utc), "yyyyMM") { }
    }

    public class DayGenerator : StringKeyGenerator
    {
        private readonly DateTime epochStart;
        private readonly string dateFormat;

        public DayGenerator(DateTime epochStart, string dateFormat)
        {
            this.epochStart = epochStart;
            this.dateFormat = dateFormat;
        }

        public virtual IEnumerable<StringKey> GetKeys()
        {
            int daysThroughPresent = (int)((DateTime.UtcNow.Date - epochStart).TotalDays + 1);
            return Enumerable.Range(0, daysThroughPresent)
                .Select(offset => epochStart.AddDays(offset))
                .Select(date => date.ToString(dateFormat))
                .Distinct()
                .Select(
                    formattedDay => new StringKey
                    {
                        Equal = formattedDay
                    });
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class StorageResourceAttribute : Attribute, IBackupStorageType
    {
        public StorageResourceAttribute() : this(typeof(NoKeyGenerator), typeof(NoKeyGenerator)) { }

        public StorageResourceAttribute(Type partitionKeyGenerator) : this(partitionKeyGenerator, typeof(NoKeyGenerator)) { }

        public StorageResourceAttribute(Type partitionKeyGenerator, Type rowKeyGenerator)
        {
            PartitionKeyGenerator =
                () => (StringKeyGenerator)Activator.CreateInstance(partitionKeyGenerator);
            RowKeyGenerator =
                () => (StringKeyGenerator)Activator.CreateInstance(rowKeyGenerator);
        }

        public Func<StringKeyGenerator> PartitionKeyGenerator { get; set; }
        public Func<StringKeyGenerator> RowKeyGenerator { get; set; }
        public string SortKey { get; set; }

        public virtual IEnumerable<StorageResourceInfo> GetStorageResourceInfos(Type t)
        {
            string GetSortKey(string tableName)
            {
                if (this.SortKey.HasBlackSpace())
                    return this.SortKey;
                return tableName;
            }

            var attr = this;
            var baseResWhereInfo = WhereInformation
                .GenerateWhereInformation(
                    this.PartitionKeyGenerator.Invoke(),
                    this.RowKeyGenerator.Invoke())
                .ToArray();
            var baseResInfo = new StorageResourceInfo
            {
                tableName = this.GetTableName(t),
                sortKey = GetSortKey(attr.GetTableName(t)),
                message = baseResWhereInfo,
            };
            return baseResInfo.AsEnumerable();
        }

        public string GetTableName(object declaringInfo)
        {
            var declaringType = (Type)declaringInfo;
            return declaringType.GetCustomAttributes<StorageTableAttribute>()
                .First(
                    (storageTableAttr, next) =>
                    {
                        if (storageTableAttr.TableName.HasBlackSpace())
                            return storageTableAttr.TableName;
                        return next();
                    },
                    () => declaringType.Name);
        }
    }


    public class StorageResourceNoOpAttribute : StorageResourceAttribute, IBackupStorageType
    {
        public override IEnumerable<StorageResourceInfo> GetStorageResourceInfos(Type t)
        {
            yield return new StorageResourceInfo
            {
                tableName = GetTableName(t),
                message = new WhereInformation[] { },
                sortKey = default,
            };
        }
    }

    
}