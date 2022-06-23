using System;
using System.Reflection;

namespace EastFive.Azure.Auth.Salesforce.Resources
{
    public class Describe
    {

        public string name;
        public Field[] fields;
        public RecordTypeInfos[] recordTypeInfos;

        public bool hasSubtypes;
        public bool isInterface;
        public bool isSubtype;
        public bool layoutable;
        public bool mergeable;
        public bool mruEnabled;
        public bool queryable;
        public string implementedBy;
        public string implementsInterfaces;
        public string keyPrefix;
        public string label;
        public string labelPlural;
        public string listviewable;
        public string lookupLayoutable;
        public string[] namedLayoutInfos;
        public string networkScopeFieldName;
    }

    public class RecordTypeInfos
    {
        public bool active;
        public bool available;
        public bool defaultRecordTypeMapping;
        public string developerName;
        public bool master;
        public string  name;
        public string recordTypeId;
    }


    public class Field
    {
        public bool aggregatable;
        public bool aiPredictionField;
        public bool autoNumber;
        public int byteLength;
        public bool calculated;
        public string calculatedFormula;
        public bool cascadeDelete;
        public bool caseSensitive;
        public string compoundFieldName;
        public string controllerName;
        public bool createable;
        public bool custom;
        public string defaultValue;
        public string defaultValueFormula;
        public bool defaultedOnCreate;
        public bool dependentPicklist;
        public bool deprecatedAndHidden;
        public int digits;
        public bool displayLocationInDecimal;
        public bool encrypted;
        public bool externalId;
        public string extraTypeInfo;
        public bool filterable;
        public string filteredLookupInfo;
        public bool formulaTreatNullNumberAsZero;
        public bool groupable;
        public bool highScaleNumber;
        public bool htmlFormatted;
        public bool idLookup;
        public string inlineHelpText;
        public string label;
        public int length;
        public string mask;
        public string maskType;
        public string name;
        public bool nameField;
        public bool namePointing;
        public bool nillable;
        public bool permissionable;
        public PicklistDescription[] picklistValues;
        public bool polymorphicForeignKey;
        public int precision;
        public bool queryByDistance;
        public string referenceTargetField;
        public string[] referenceTo;
        public string relationshipName;
        public string relationshipOrder;
        public bool restrictedDelete;
        public bool restrictedPicklist;
        public int scale;
        public bool searchPrefilterable;
        public string soapType;
        public bool sortable;

        //public enum FieldType
        //{
        //    id,
        //    boolean,
        //    currency,
        //    @string,
        //    datetime,
        //    date,
        //    @double,
        //    @int,
        //    percent,
        //    reference,
        //    picklist,
        //    multipicklist,
        //    textarea,
        //    address,
        //    phone,
        //    email,
        //    url,
        //}

        public string type;
        public bool unique;
        public bool updateable;
        public bool writeRequiresMasterRead;

        public class PicklistDescription
        {
            public bool active;
            public bool defaultValue;
            public string label;
            public string value;
        }
    }
}

