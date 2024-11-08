using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Reflection;

using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Persistence.StorageTables
{
    public class StoreDelimitedAttribute : StorageAttribute
    {
        public string Delimiter { get; set; } = ",";

        public string GetIndexKey(string propertyName) =>
            $"{propertyName}_DL";

        public override KeyValuePair<string, EntityProperty>[] CastValue(Type typeOfValue, object values, string propertyName)
        {
            if(typeOfValue.IsAssignableTo(typeof(IReferences)))
            {
                var standardIRefValues = base.CastValue(typeOfValue, values, propertyName);
                var references = (IReferences)values;
                var newIRefValue = references.ids
                    .Select(
                        (value) =>
                        {
                            return value.ToString("N");
                        })
                    .Join(this.Delimiter);

                var newPropNameIRefs = GetIndexKey(propertyName);
                return standardIRefValues
                    .Append(newPropNameIRefs.PairWithValue(EntityProperty.GeneratePropertyForString(newIRefValue)))
                    .ToArray();
            }

            if (!typeOfValue.IsArray)
                return base.CastValue(typeOfValue, values, propertyName);

            if (values == null)
                return new KeyValuePair<string, EntityProperty>[] { };

            var standardValues = base.CastValue(typeOfValue, values, propertyName);
            var elementType = typeOfValue.GetElementType();
            var newPropName = GetIndexKey(propertyName);
            var newValue = ((IEnumerable)values)
                .Cast<object>()
                .Select(
                    (value, index) =>
                    {
                        return value.ToString();
                    })
                .Join(this.Delimiter);

            return standardValues
                .Append(newPropName.PairWithValue(EntityProperty.GeneratePropertyForString(newValue)))
                .ToArray();
        }
    }
}
