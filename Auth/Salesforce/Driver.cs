using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Net;
using EastFive.Reflection;
using EastFive.Serialization;
using EastFive.Serialization.Json;
using EastFive.Web.Configuration;
using EastFive.Azure.Auth.Salesforce.Resources;

namespace EastFive.Azure.Auth.Salesforce
{
	public interface IDefineSalesforceApiPath
    {
		Uri ProvideUrl(string instanceUrl);
    }

    public interface IBindSalesforce
    {
		/// <summary>
		/// 
		/// </summary>
		/// <param name="member"></param>
		/// <param name="field"></param>
		/// <param name="primaryResource">The resource that is being synchronized</param>
		/// <remarks>
		/// <paramref name="primaryResource"/> Can be used to differentiate
		/// if this class is used to differentiate the case where this class is used as an extraObjct,
        /// or if this class is the primary object being synchronized</remarks>
		/// <returns></returns>
		bool IsMatch(MemberInfo member, Field field, Type primaryResource);

        void PopluateSalesforceResource(JsonTextWriter jsonWriter, MemberInfo member, object resource, Field field);
    }

    public class SalesforceApiPathAttribute : Attribute, IDefineSalesforceApiPath
	{
		public SalesforceApiPathAttribute(string objectName)
        {
			this.ObjectName = objectName;
		}

		public string ObjectName { get; set; }

        public Uri ProvideUrl(string instanceUrl)
        {
			Uri.TryCreate($"{instanceUrl}/services/data/v54.0/sobjects/{this.ObjectName}/", UriKind.Absolute, out Uri url);
			return url;
		}
    }

    public class SalesforcePropertyAttribute : Attribute, IBindSalesforce
    {
		public string Name { get; set; }

		public Type Type { get; set; }

		public SalesforcePropertyAttribute(string name)
		{
			this.Name = name;
		}

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
    }

	public class SalesforceProperty2Attribute : SalesforcePropertyAttribute
    {
		public SalesforceProperty2Attribute(string name) : base(name)
        {

        }
	}

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

	public class Driver
	{
		string authToken;
		string instanceUrl;
		string tokenType;
		string refreshToken;

		public Driver(string instanceUrl, string authToken, string tokenType, string refreshToken)
		{
			this.instanceUrl = instanceUrl;
			this.authToken = authToken;
			this.tokenType = tokenType;
			this.refreshToken = refreshToken;
		}

		public Task<TResult> RefreshToken<TResult>(
			Func<string, TResult> onRefreshed,
			Func<string, TResult> onFailure)
        {
			return EastFive.Azure.AppSettings.Auth.Salesforce.ConsumerKey.ConfigurationString(
				clientId =>
				{
					return EastFive.Azure.AppSettings.Auth.Salesforce.ConsumerSecret.ConfigurationString(
						clientSecret =>
						{
							Uri.TryCreate($"{this.instanceUrl}/services/oauth2/token", UriKind.Absolute, out Uri refreshUrl);
							return refreshUrl.HttpPostFormDataContentAsync(
								new Dictionary<string, string>()
								{
									{ "client_id", clientId },
									{ "client_secret", clientSecret },
									{ "refresh_token", this.refreshToken },
									{ "grant_type", "refresh_token" },
								},
								(SalesforceTokenResponse response) =>
								{
									this.authToken = response.access_token;
									return onRefreshed(response.access_token);
								},
								onFailure: onFailure);
						});
				});
			
		}

		public async Task<TResult> DescribeAsync<TResource, TResult>(
			Func<Resources.Describe, TResult> onCreated,
			Func<HttpStatusCode, string, TResult> onFailure = default)
		{
			var attr = typeof(TResource).GetAttributeInterface<IDefineSalesforceApiPath>();
			var location = attr.ProvideUrl(this.instanceUrl);
			var describeLocation = location.AppendToPath("describe");

			return await describeLocation.HttpClientGetResourceAsync(
					authToken: this.authToken, tokenType: this.tokenType,
				(Resources.Describe response) =>
				{
					return onCreated(response);
				},
				onFailureWithBody: (statusCode, body) =>
				{
					return onFailure(statusCode, body);
				},
					didTokenGetRefreshed: this.DidTokenGetRefreshed);
		}

		public async Task<TResult> CreateAsync<TResource, TResult>(TResource resource,
			Func<string, TResult> onCreated,
			Func<HttpStatusCode, string, TResult> onFailure = default)
		{
			typeof(TResource).TryGetAttributeInterface(out IDefineSalesforceApiPath attr);
			var location = attr.ProvideUrl(this.instanceUrl);

			return await location.HttpClientPostResourceAsync(resource,
					authToken: this.authToken, tokenType: this.tokenType,
				(Response response) =>
				{
					var id = response.id;
					return onCreated(id);
				},
				onFailureWithBody: (statusCode, body) =>
				{
					return ProcessCreateFailure(statusCode, body, onCreated, onFailure);
				},
					didTokenGetRefreshed: this.DidTokenGetRefreshed);
		}

		public async Task<TResult> SynchronizeAsync<TResource, TResult>(TResource resource,
				object[] extraResources, Dictionary<string, object> extraValues,
			Func<string, TResult> onCreated,
			Func<HttpStatusCode, string, TResult> onFailure = default)
		{
			return await await this.DescribeAsync<TResource, Task<TResult>>(
				async description =>
				{
					var attr = typeof(TResource).GetAttributeInterface<IDefineSalesforceApiPath>();
					var location = attr.ProvideUrl(this.instanceUrl);

					var json = Serialize();

					return await location.HttpClientPostDynamicRequestAsync(
							populateRequest:(request) =>
                            {
								var content = new StringContent(json,
									encoding: System.Text.Encoding.UTF8,
									mediaType:"application/json");
								request.Content = content;
								return (request, () => { content.Dispose(); });
							},
							authToken: this.authToken, tokenType: this.tokenType,
						(Response response) =>
						{
							var id = response.id;
							return onCreated(id);
						},
						onFailureWithBody: (statusCode, body) =>
						{
							return ProcessCreateFailure(statusCode, body, onCreated, onFailure);
						},
							didTokenGetRefreshed: this.DidTokenGetRefreshed);

					string Serialize()
                    {
						var stringBuilder = new System.Text.StringBuilder();
						using (var textWriter = new System.IO.StringWriter(stringBuilder))
						{
							using (var jsonWriter = new JsonTextWriter(textWriter))
							{
								jsonWriter.WriteStartObject();

								SerializeObject(typeof(TResource), resource, jsonWriter, typeof(TResource));
								foreach(var extraRes in extraResources.NullToEmpty())
                                {
									var type = extraRes.GetType();
									SerializeObject(type, extraRes, jsonWriter, typeof(TResource));
								}

								foreach(var extraValue in extraValues.NullToEmpty())
                                {
									jsonWriter.WritePropertyName(extraValue.Key);
									jsonWriter.WriteValue(extraValue.Value);
								}
								
								jsonWriter.WriteEndObject();
								var json = stringBuilder.ToString();
								return json;
							}
						}
					}

					void SerializeObject(Type type, object resourceToSerialize, JsonTextWriter jsonWriter, Type originalType)
                    {
						var (matched, discard1, discard2) = type
							.GetPropertyAndFieldsWithAttributesInterface<IBindSalesforce>(multiple:true)
							.Match(description.fields,
								(memberAttrTpl, field) =>
								{
									var (member, attr) = memberAttrTpl;
									return attr.IsMatch(member, field, originalType);
								});

						foreach (var ((member, binder), field) in matched)
						{
							binder.PopluateSalesforceResource(jsonWriter, member, resourceToSerialize, field);
						}
					}
				},
				(code, why) =>
				{
					return onFailure(code, why).AsTask();
				});
		}


		protected static TResult ProcessCreateFailure<TResult>(HttpStatusCode statusCode, string body,
			Func<string, TResult> onDuplicate,
			Func<HttpStatusCode, string, TResult> onFailure)
        {
			return body.JsonParse(
				(ErrorResponse[] errorResponses) =>
				{
					return errorResponses.First(
						(errorResponse, next) =>
						{
							if (errorResponse.errorCode != ErrorCodes.DUPLICATES_DETECTED)
								return next();

							if (errorResponse.duplicateResut.IsDefaultOrNull())
								return next();

							return errorResponse.duplicateResut.matchResults
								.NullToEmpty()
								.First(
									(dup, next) =>
									{
										if (!dup.success)
											return next();

										return dup.matchRecords
											.NullToEmpty()
											.First(
												(dupMatch, nextDupMatch) =>
												{
													if (dupMatch.record.IsDefaultOrNull())
														return nextDupMatch();

													return onDuplicate(dupMatch.record.Id);
												},
												() => OnFailure());
									},
									() => OnFailure());

						},
						() =>
                        {
							return errorResponses.First(
								(errorResponse, next) =>
								{
									if (errorResponse.errorCode != ErrorCodes.DUPLICATE_VALUE)
										return next();

									// duplicate value found: AffirmId__c duplicates value on record with id: 0018M000003uKqq
									return errorResponse.message.MatchRegexInvoke(
										"duplicate value.*:\\s*(?<property>[0-9a-zA-Z_]+)\\s*duplicates.*:\\s*(?<value>[0-9a-zA-Z]+)",
										(property, value) => new { property, value },
										matches =>
										{
											return matches.First(
												(match, nextMatch) =>
												{
														var sfId = match.value;
														return onDuplicate(sfId);
												},
												() => OnFailure());
										});

									
								},
								() => OnFailure());
						});
						
				},
				(message) => onFailure(statusCode, message));

			TResult OnFailure()
			{
				if (onFailure.IsDefaultOrNull())
					throw new Exception($"[{statusCode}]:{body}");
				return onFailure(statusCode, body);
			}
		}

		async Task<(bool, string)> DidTokenGetRefreshed(HttpStatusCode statusCode, string body)
        {
			if (statusCode != HttpStatusCode.Unauthorized)
				return (false, string.Empty);

			return await body.JsonParse(
				(ErrorResponse[] errorResponses) =>
				{
					return errorResponses.First(
						async (errorResponse, next) =>
						{
							// [{"message":"Session expired or invalid","errorCode":"INVALID_SESSION_ID"}]

							if (errorResponse.errorCode != ErrorCodes.INVALID_SESSION_ID)
								return (false, string.Empty);

							if (!errorResponse.message.Contains("expired", StringComparison.OrdinalIgnoreCase))
								return (false, string.Empty);

							return await this.RefreshToken(
								onRefreshed: (newToken) =>
								{
									return (true, newToken);
								},
								onFailure: (why) => (false, string.Empty));
						},
						() => (false, string.Empty).AsTask());
				},
				(message) => (false, string.Empty).AsTask());
		}

		public enum ErrorCodes
        {
			INVALID_SESSION_ID,
			DUPLICATES_DETECTED,
			DUPLICATE_VALUE,

			//{"message":"insufficient access rights on cross-reference id: 0015e00000tW1f0","errorCode":"INSUFFICIENT_ACCESS_ON_CROSS_REFERENCE_ENTITY","fields":[]}
			INSUFFICIENT_ACCESS_ON_CROSS_REFERENCE_ENTITY,

			//{"message":"Record Type ID: this ID value isn't valid for the user: 0128M0000004HVOQA2","errorCode":"INVALID_CROSS_REFERENCE_KEY","fields":["RecordTypeId"]}
			INVALID_CROSS_REFERENCE_KEY,

			//{"message":"Unable to create/update fields: Name. Please check the security settings of this field and verify that it is read/write for your profile or permission set.","errorCode":"INVALID_FIELD_FOR_INSERT_UPDATE","fields":["Name"]}
			INVALID_FIELD_FOR_INSERT_UPDATE,

			//{"message":"Job Category: bad value for restricted picklist field: NURSE PRACTITIONER, SUPERVISING(NP,S)","errorCode":"INVALID_OR_NULL_FOR_RESTRICTED_PICKLIST","fields":["Job_Category__c"]}
			INVALID_OR_NULL_FOR_RESTRICTED_PICKLIST,
		}

		internal class ErrorResponse
        {
			public string message;
			public ErrorCodes errorCode;
			public DuplicateResult duplicateResut;
			public object[] fields;
		}

		// {"id":"0018M000001oWgPQAU","success":true,"errors":[]}

		internal class Response
        {
			public string id;
			public bool success;
			public string[] errors;
		}

		internal class DuplicateResult
		{
			public string duplicateRule;
			public bool allowSave;
			public string duplicateRuleEntityType;
			public string errorMessage;
			public MatchResult[] matchResults;
		}

		internal class MatchResult
        {
			public string entityType;
			public string[] errors;
			public string matchEngine;
			public MatchRecord[] matchRecords;
			public bool success;
		}

		internal class MatchRecord
        {
			public Record record;
		}

		internal class Record
        {
			public string Id;
        }

		// [{"duplicateResut":{"allowSave":true,"duplicateRule":"Standard_Contact_Duplicate_Rule","duplicateRuleEntityType":"Contact","errorMessage":"You're creating a duplicate record. We recommend you use an existing record instead.","matchResults":[{"entityType":"Contact","errors":[],"matchEngine":"FuzzyMatchEngine","matchRecords":[{"additionalInformation":[],"fieldDiffs":[],"matchConfidence":100.0,"record":{"attributes":{"type":"Contact","url":"/services/data/v54.0/sobjects/Contact/0038M0000016Wv8QAE"},"Id":"0038M0000016Wv8QAE"}}],"rule":"Standard_Contact_Match_Rule_v1_1","size":1,"success":true}]},"errorCode":"DUPLICATES_DETECTED","message":"You're creating a duplicate record. We recommend you use an existing record instead."}]

		//[SalesforceApiPath("Lead")]
		//public class Leads
		//{
		//	public string Salutation;
		//	public string FirstName;
		//	public string LastName;
		//	public string Company;
		//	public string Title;
		//	public string Email;

		//	/// <summary>
		//	/// Must contain <code>Name</code> property
		//	/// </summary>
		//	public object RecordType;
		//	public string RecordTypeId;
		//}

		//public class Contacts
		//{
		//	private Driver driver;

		//	public string Salutation;
		//	public string FirstName;
		//	public string LastName;
		//	// public string Company;
		//	public string Title;
		//	public string Email;

		//	[JsonProperty(PropertyName = "AccountId")]
		//	public string Account;


		//	/// <summary>
		//	/// Must contain <code>Name</code> property
		//	/// </summary>
		//	public object RecordType;
		//	public string RecordTypeId;

		//	public Contacts(Driver driver)
		//	{
		//		this.driver = driver;
		//	}

		//	public async Task<TResult> CreateAsync<TResult>(
		//		Func<string, TResult> onCreated,
		//		Func<HttpStatusCode, string, TResult> onFailure = default)
		//	{
		//		Uri.TryCreate($"{this.driver.instanceUrl}/services/data/v54.0/sobjects/Contact/", UriKind.Absolute, out Uri leadLocation);

		//		return await leadLocation.HttpClientPostResourceAsync(this,
		//				authToken: this.driver.authToken, tokenType: this.driver.tokenType,
		//			(Response response) =>
		//			{
		//				var id = response.id;
		//				return onCreated(id);
		//			},
		//			onFailureWithBody: (statusCode, body) =>
		//			{
		//				return ProcessFailure(statusCode, body, onCreated, onFailure);
		//			},
		//				didTokenGetRefreshed: driver.DidTokenGetRefreshed);
		//	}
		//}

		//public class Logins
		//{
		//	private Driver driver;

		//	[JsonProperty(PropertyName = "Login_Date__c")]
		//	public DateTime When;

		//	[JsonProperty(PropertyName = "Login_User__c")]
		//	public string User;

		//	[JsonProperty(PropertyName = "LoginDayIndex__c")]
		//	public string LoginDayIndex => $"{When.Year}|{When.DayOfYear}";

		//	public Logins(Driver driver)
		//	{
		//		this.driver = driver;
		//	}

		//	public async Task<TResult> CreateAsync<TResult>(
		//		Func<string, TResult> onCreated,
		//		Func<HttpStatusCode, string, TResult> onFailure = default)
		//	{
		//		Uri.TryCreate($"{this.driver.instanceUrl}/services/data/v54.0/sobjects/Logins__c/", UriKind.Absolute, out Uri leadLocation);

		//		return await leadLocation.HttpClientPostResourceAsync(this,
		//				authToken: this.driver.authToken, tokenType: this.driver.tokenType,
		//			(Response response) =>
		//			{
		//				var id = response.id;
		//				return onCreated(id);
		//			},
		//			onFailureWithBody: (statusCode, body) =>
		//			{
		//				return ProcessFailure(statusCode, body, onCreated, onFailure);
		//			},
		//				didTokenGetRefreshed: driver.DidTokenGetRefreshed);

		//	}
		//}

		//public class Accounts
		//{
		//	private Driver driver;

		//	public string Name;

		//	public string Type;

		//	public string AccountSource;

		//	public string Description;

		//	[JsonProperty(PropertyName = "EMR__c")]
		//	public string EMR;

		//	public string ParentId;

		//	[JsonProperty(PropertyName = "City__c")]
		//	public string City;

		//	[JsonProperty(PropertyName = "State__c")]
		//	public string State;

		//	[JsonProperty(PropertyName = "Street_Address__c")]
		//	public string StreetAddress;

		//	[JsonProperty(PropertyName = "Zip__c")]
		//	public string Zip;

		//	public Accounts(Driver driver)
		//	{
		//		this.driver = driver;
		//	}

		//	public async Task<TResult> CreateAsync<TResult>(
		//		Func<string, TResult> onCreated,
		//		Func<HttpStatusCode, string, TResult> onFailure = default)
		//	{
		//		Uri.TryCreate($"{this.driver.instanceUrl}/services/data/v54.0/sobjects/Account/", UriKind.Absolute, out Uri leadLocation);

		//		return await leadLocation.HttpClientPostResourceAsync(this,
		//				authToken: this.driver.authToken, tokenType: this.driver.tokenType,
		//			(Response response) =>
		//			{
		//				var id = response.id;
		//				return onCreated(id);
		//			},
		//			onFailureWithBody: (statusCode, body) =>
		//			{
		//				return ProcessFailure(statusCode, body, onCreated, onFailure);
		//			},
		//				didTokenGetRefreshed: driver.DidTokenGetRefreshed);
		//	}
		//}
	}
}

