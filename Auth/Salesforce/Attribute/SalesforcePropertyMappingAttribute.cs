using System;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

using EastFive;
using EastFive.Azure.Auth.Salesforce.Resources;
using EastFive.Extensions;
using EastFive.Reflection;
using EastFive.Linq;

namespace EastFive.Azure.Auth.Salesforce
{
	public class SalesforcePropertyMappingAttribute : SalesforcePropertyAttribute
	{
		public bool IgnoreCase { get; set; } = true;
		public string Key1 { get; set; }
		public string Value1 { get; set; }
		public string Key2 { get; set; }
		public string Value2 { get; set; }
		public string Key3 { get; set; }
		public string Value3 { get; set; }
		public string Key4 { get; set; }
		public string Value4 { get; set; }
		public string Key5 { get; set; }
		public string Value5 { get; set; }
		public string Key6 { get; set; }
		public string Value6 { get; set; }
		public string Key7 { get; set; }
		public string Value7 { get; set; }
		public string Default { get; set; }

		public SalesforcePropertyMappingAttribute(string name) : base(name)
		{

		}

		public override void PopluateSalesforceResource(JsonTextWriter jsonWriter, MemberInfo member, object resource, Field field)
		{
			var value = (string)member.GetPropertyOrFieldValue(resource);
			var propsAndFields = typeof(SalesforcePropertyMappingAttribute)
				.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.ToArray();

			bool didWrite = propsAndFields
				.Where(prop => prop.Name.StartsWith("Key"))
				.First(
					(keyProp, next) =>
					{
						var keyValue = (string)keyProp.GetValue(this);
						if (!value.Equals(keyValue, StringComparison.OrdinalIgnoreCase))
							return next();

						var valueProp = propsAndFields
							.Where(prop => prop.Name.StartsWith("Value"))
							.Where(valueProp => valueProp.Name.Last() == keyProp.Name.Last())
							.First();
						var valueValue = valueProp.GetValue(this);

						jsonWriter.WritePropertyName(this.Name);
						jsonWriter.WriteValue(valueValue);

						return true;
					},
					() =>
					{
						if (Default.IsNullOrWhiteSpace())
							return false;

						jsonWriter.WritePropertyName(this.Name);
						jsonWriter.WriteValue(Default);

						return true;
					});
		}
	}
}

