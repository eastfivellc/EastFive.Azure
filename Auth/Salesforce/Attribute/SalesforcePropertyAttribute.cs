using System;
using System.Reflection;

using Newtonsoft.Json;

using EastFive.Azure.Auth.Salesforce.Resources;
using EastFive.Extensions;
using EastFive.Reflection;
using Newtonsoft.Json.Linq;

namespace EastFive.Azure.Auth.Salesforce
{
	public class SalesforcePropertyAttribute : Attribute, IBindSalesforce, ICastSalesforce
	{
		public string Name { get; set; }

		public Type Type { get; set; }

		public StringComparison ComparisonMethod { get; set; } = StringComparison.Ordinal;

		public SalesforcePropertyAttribute(string name)
		{
			this.Name = name;
		}

		#region IBindSalesforce

		public virtual bool IsMatch(MemberInfo member, Field field, Type primaryResource)
		{
			if (Type.IsNotDefaultOrNull())
				if (Type != primaryResource)
					return false;

			return Name.Equals(field.name, StringComparison.Ordinal);
		}

		public virtual void PopluateSalesforceResource(JsonTextWriter jsonWriter,
			MemberInfo member, object resource, Field field)
		{
			jsonWriter.WritePropertyName(field.name);
			var value = member.GetPropertyOrFieldValue(resource);

			WriteJson(jsonWriter, value, new JsonSerializer());

			void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
			{
				Api.Serialization.ExtrudeConvert.WriteJson(jsonWriter, value, serializer,
					type => false,
					(wtr, v, serializer) => WriteJson(wtr, v, serializer),
					serializer => 1.0);
			}
		}

		#endregion

		#region ICastSalesforce

		public bool IsMatch(MemberInfo member, JProperty jproperty, Type primaryResource)
		{
			if (Type.IsNotDefaultOrNull())
				if (Type != primaryResource)
					return false;

			return Name.Equals(jproperty.Name, StringComparison.Ordinal);
		}

        public virtual void PopluateSalesforceResource(object resource,
			MemberInfo member, JObject jsonObject, JProperty jProperty,
			bool overrideEmptyValues)
		{
			var serializer = new JsonSerializer();
			var reader = jProperty.CreateReader();
			var memberType = member.GetPropertyOrFieldType();

			var currentValue = member.GetPropertyOrFieldValue(resource);
			var memberValue = EastFive.Serialization.Json.Converter.ReadJsonStatic(
				reader, memberType, currentValue, serializer);

			if (!overrideEmptyValues)
				if (memberValue.IsDefaultOrNull())
					return;

			member.TrySetPropertyOrFieldValue(resource, memberValue);
		}

        #endregion
    }

    public class SalesforceProperty2Attribute : SalesforcePropertyAttribute
	{
		public SalesforceProperty2Attribute(string name) : base(name)
		{

		}
	}
}

