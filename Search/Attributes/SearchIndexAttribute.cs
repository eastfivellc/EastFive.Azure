using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using EastFive;
using EastFive.Serialization;
using EastFive.Linq;
using EastFive.Extensions;

namespace EastFive.Azure.Search
{
    public class SearchIndexAttribute : Attribute, IProvideSearchIndex
    {
        public string Index { get; set; }

        public string GetIndexName(Type type)
        {
            var safeIndex = Index
                .Select(
                    (c, i) =>
                    {
                        if (!Char.IsUpper(c))
                            return c.ToString();
                        if (i == 0)
                            return Char.ToLower(c).ToString();

                        return $"_{Char.ToLower(c)}";
                    })
                .ToArray()
                .Join("");
            return safeIndex;
        }
    }
}
