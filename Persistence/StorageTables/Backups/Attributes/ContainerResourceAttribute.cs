using EastFive.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Persistence.Attributes
{
    public interface PrefixGenerator
    {
        IEnumerable<string> GetPrefixes();
    }

    public class NoPrefixGenerator : ListPrefixGenerator
    {
        public NoPrefixGenerator() : base(new string [] { }) { }
    }

    public class ListPrefixGenerator : PrefixGenerator
    {
        private readonly IEnumerable<string> items;

        public ListPrefixGenerator(IEnumerable<string> items)
        {
            this.items = items;
        }

        public virtual IEnumerable<string> GetPrefixes()
        {
            return items
                .NullToEmpty();
        }
    }

    public class OnePlaceHexadecimalPrefixGenerator : HexadecimalPrefixGenerator
    {
        public OnePlaceHexadecimalPrefixGenerator() : base(1) { }
    }

    public class TwoPlaceHexadecimalPrefixGenerator : HexadecimalPrefixGenerator
    {
        public TwoPlaceHexadecimalPrefixGenerator() : base(2) { }
    }

    public class ThreePlaceHexadecimalPrefixGenerator : HexadecimalPrefixGenerator
    {
        public ThreePlaceHexadecimalPrefixGenerator() : base(3) { }
    }

    public class HexadecimalPrefixGenerator : PrefixGenerator
    {
        private readonly string format;
        private readonly int count;

        public HexadecimalPrefixGenerator(int places)
        {
            this.format = $"x{places}";
            this.count = 0x1 << (places * 4);
        }

        public virtual IEnumerable<string> GetPrefixes()
        {
            return Enumerable.Range(0, count)
            .Select(num => num.ToString(format));
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class ContainerResourceAttribute : Attribute
    {
        public ContainerResourceAttribute(string name) : this(name, typeof(NoPrefixGenerator)) { }

        public ContainerResourceAttribute(string name, Type prefixGenerator)
        {
            Name = name;
            PrefixGenerator =
                () => (PrefixGenerator)Activator.CreateInstance(prefixGenerator);
        }

        public string Name { get; set; }
        public Func<PrefixGenerator> PrefixGenerator { get; set; }
    }
}
