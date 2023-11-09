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
        string GetKeyName(MemberInfo member);

        public SearchField GetSearchField(MemberInfo member);

    }

    public class SearchFieldAttribute : Attribute, IProvideSearchField
    {
        public string Name { get; set; }

        public bool IsFilterable { get; set; }
        public bool IsSortable { get; set; }
        public bool IsSearchable { get; set; }

        public SearchField GetSearchField(MemberInfo member)
        {
            var memberType = member.GetPropertyOrFieldType();
            var name = GetKeyName(member);
            if (typeof(int).IsAssignableFrom(memberType))
            {
                var field = new SimpleField(name, SearchFieldDataType.Int32);
                return PopulateFieldKey(field);
            }
            if (typeof(DateTime).IsAssignableFrom(memberType))
            {
                var field = new SimpleField(name, SearchFieldDataType.DateTimeOffset);
                return PopulateFieldKey(field);
            }
            var searchableField = new SearchableField(name);
            return PopulateFieldKey(searchableField);
        }

        public string GetKeyName(MemberInfo member)
        {
            if (this.Name.HasBlackSpace())
                return this.Name;

            return member.Name;
        }

        protected virtual SearchField PopulateFieldKey(SearchField searchField)
        {
            searchField.IsSearchable = this.IsSearchable;
            searchField.IsFilterable = this.IsFilterable;
            searchField.IsSortable = this.IsSortable;
            return searchField;
        }
    }
}
