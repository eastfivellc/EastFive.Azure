using Azure.Search.Documents.Indexes.Models;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using EastFive.Reflection;

namespace EastFive.Azure.Search
{
    public interface IProvideSearchField
    {
        string Name { get; }

        public SearchField GetSearchField(MemberInfo member);

    }

    public class SearchFieldAttribute : Attribute, IProvideSearchField
    {
        public string Name { get; set; }

        public bool IsFilterable { get; set; }
        public bool IsSortable { get; set; }

        public SearchField GetSearchField(MemberInfo member)
        {
            var memberType = member.GetPropertyOrFieldType();
            if (typeof(int).IsAssignableFrom(memberType))
            {
                var field = new SimpleField(this.Name, SearchFieldDataType.Int32);
                return PopulateFieldKey(field);
            }
            if (typeof(DateTime).IsAssignableFrom(memberType))
            {
                var field = new SimpleField(this.Name, SearchFieldDataType.DateTimeOffset);
                return PopulateFieldKey(field);
            }
            var searchableField = new SearchableField("hotelName");
            return PopulateFieldKey(searchableField);
        }

        protected virtual SearchField PopulateFieldKey(SearchField searchField)
        {
            searchField.IsFilterable = this.IsFilterable;
            searchField.IsSortable = this.IsSortable;
            return searchField;
        }
    }
}
