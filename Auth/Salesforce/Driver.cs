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
using Newtonsoft.Json.Linq;
using System.Threading;
using EastFive.Collections.Generic;

namespace EastFive.Azure.Auth.Salesforce
{
	public interface IDefineSalesforceApiPath
    {
		Uri ProvideUrl(string instanceUrl);
    }

	public interface IDefineSalesforceIdentifier
    {
		TResult GetIdentifier<T, TResult>(T resource, MemberInfo propertyOrField,
			Func<string, TResult> onIdentified,
			Func<TResult> onNoIdentification);
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


	public interface ICastSalesforce
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
		bool IsMatch(MemberInfo member, JProperty jproperty, Type primaryResource);

		void PopluateSalesforceResource(object resource, MemberInfo member, JObject jsonObject, JProperty jProperty, bool overrideEmptyValues);
	}

	public class Driver
	{
		string authToken;
		string instanceUrl;
		string tokenType;
		string refreshToken;
		DateTime lastRefreshed;
		AutoResetEvent tokenLock = new AutoResetEvent(true);

		public Driver(string instanceUrl, string authToken, string tokenType, string refreshToken)
		{
			this.instanceUrl = instanceUrl;
			this.authToken = authToken;
			this.tokenType = tokenType;
			this.refreshToken = refreshToken;
			this.lastRefreshed = DateTime.UtcNow;
		}

		public Task<TResult> RefreshToken<TResult>(
			Func<string, TResult> onRefreshed,
			Func<string, TResult> onFailure)
        {
			return EastFive.Azure.AppSettings.Auth.Salesforce.ConsumerKey.ConfigurationString(
				clientId =>
				{
					return EastFive.Azure.AppSettings.Auth.Salesforce.ConsumerSecret.ConfigurationString(
						async clientSecret =>
						{
							Uri.TryCreate($"{this.instanceUrl}/services/oauth2/token", UriKind.Absolute, out Uri refreshUrl);
							var startAttempt = DateTime.Now;

							tokenLock.WaitOne();

							if (startAttempt > this.lastRefreshed)
							{
								tokenLock.Set();
								return onRefreshed(this.authToken);
							}

							return await refreshUrl.HttpPostFormDataContentAsync(
									new Dictionary<string, string>()
									{
										{ "client_id", clientId },
										{ "client_secret", clientSecret },
										{ "refresh_token", this.refreshToken },
										{ "grant_type", "refresh_token" },
									},
								(SalesforceTokenResponse response) =>
								{
									this.lastRefreshed = DateTime.UtcNow;
									this.authToken = response.access_token;
									tokenLock.Set();
									return onRefreshed(response.access_token);
								},
								onFailure: why =>
								{
									tokenLock.Set();
									return onFailure(why);
								});
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

		public async Task<TResult> GetAsync<TResource, TResult>(
				TResource resource, object [] extraResources, IDictionary<string, object> extraValues,
			Func<TResource, object[], IDictionary<string, object>, TResult> onCreated,
			Func<HttpStatusCode, string, TResult> onFailure = default,
				bool overrideEmptyValues = false)
		{
			var originalType = typeof(TResource);
			var attr = originalType.GetAttributeInterface<IDefineSalesforceApiPath>();
			var location = attr.ProvideUrl(this.instanceUrl);
			var (member, identifierDefinition) = originalType
				.GetPropertyAndFieldsWithAttributesInterface<IDefineSalesforceIdentifier>( )
				.Single();
			return await identifierDefinition.GetIdentifier(resource, member,
				async identifier =>
                {
					var locationWithIdentifier = location.AppendToPath(identifier);

					return await await locationWithIdentifier.HttpClientGetAuthenticatedAsync(
							authToken: this.authToken, tokenType: this.tokenType,
						async (responseSuccess) =>
						{
							var content = await responseSuccess.Content.ReadAsStringAsync();
							var jObject = JObject.Parse(content);
							var resourceUpdated = (TResource)DeserializeObject(resource, jObject);
							var updatedObjects = extraResources
								.NullToEmpty()
								.Select(
									objectToUpdate =>
									{
										return DeserializeObject(objectToUpdate, jObject);
									})
								.ToArray();
							var updatedDictionary = DeserializeDictionary(extraValues, jObject);
							return onCreated(resourceUpdated, updatedObjects, updatedDictionary);
						},
						onFailureWithBody: (statusCode, body) =>
						{
							return onFailure(statusCode, body).AsTask();
						},
							didTokenGetRefreshed: this.DidTokenGetRefreshed);
				},
				() => onFailure(HttpStatusCode.NotFound, "Resource is not linked to Salesforce").AsTask());
			

			object DeserializeObject(object resourceToDeserialize, JObject jsonObject)
			{
				var (matched, discard1, discard2) = resourceToDeserialize
					.GetType()
					.GetPropertyAndFieldsWithAttributesInterface<ICastSalesforce>(multiple: true)
					.Match(jsonObject.Properties(),
						(memberAttrTpl, jproperty) =>
						{
							var (member, attr) = memberAttrTpl;
							return attr.IsMatch(member, jproperty, originalType);
						});

				foreach (var ((member, binder), jproperty) in matched)
				{
					binder.PopluateSalesforceResource(resourceToDeserialize, member, jsonObject, jproperty,
						overrideEmptyValues:overrideEmptyValues);
				}
				return resourceToDeserialize;
			}

			IDictionary<string, object> DeserializeDictionary(IDictionary<string, object> dictionaryToDeserialize, JObject jsonObject)
			{
				var (matched, discard1, discard2) = dictionaryToDeserialize
					.NullToEmpty()
					.Match(jsonObject.Properties(),
						(kvp, jproperty) =>
						{
							return kvp.Key.Equals(jproperty.Name, StringComparison.Ordinal);
						});

				return matched
					.Select(
						match =>
						{
							var (kvp, property) = match;
							if(property.Value is JValue)
                            {
								var value = (JValue)property.Value;
								return kvp.Key.PairWithValue(value.Value);
							}
							return kvp.Key.PairWithValue(default(object));
						})
					.ToDictionary();
			}
		}

		public async Task<TResult> UpdateAsync<TResource, TResult>(TResource resource,
				object[] extraResources, Dictionary<string, object> extraValues,
			Func<string, TResult> onUpdated,
			Func<HttpStatusCode, string, TResult> onFailure = default)
		{
			return await await this.DescribeAsync<TResource, Task<TResult>>(
				async description =>
				{
					var attr = typeof(TResource).GetAttributeInterface<IDefineSalesforceApiPath>();
					var location = attr.ProvideUrl(this.instanceUrl);
					var json = Serialize(resource,
						extraResources: extraResources, extraValues: extraValues, description.fields);

					return await await location.HttpClientPatchDynamicAuthenticatedAsync(
							populateRequest: (request) =>
							{
								var content = new StringContent(json,
									encoding: System.Text.Encoding.UTF8,
									mediaType: "application/json");
								request.Content = content;
								return (request, () => { content.Dispose(); });
							},
							authToken: this.authToken, tokenType: this.tokenType,
						(Response response) =>
						{
							var id = response.id;
							return onUpdated(id).AsTask();
						},
						onFailureWithBody: (statusCode, body) =>
						{
							return onFailure(statusCode, body).AsTask();
						},
							didTokenGetRefreshed: this.DidTokenGetRefreshed);

				},
				(code, why) =>
				{
					return onFailure(code, why).AsTask();
				});
		}

		public async Task<TResult> SynchronizeAsync<TResource, TResult>(TResource resource,
				object[] extraResources, Dictionary<string, object> extraValues,
			Func<string, TResult> onSynchronized,
			Func<HttpStatusCode, string, TResult> onFailure = default,
				bool forceUpdate = false)
		{
			return await await this.DescribeAsync<TResource, Task<TResult>>(
				async description =>
				{
					var attr = typeof(TResource).GetAttributeInterface<IDefineSalesforceApiPath>();
					var location = attr.ProvideUrl(this.instanceUrl);

					var json = Serialize(resource,
						extraResources:extraResources, extraValues:extraValues, description.fields);

					return await await location.HttpClientPostDynamicRequestAsync(
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
							return onSynchronized(id).AsTask();
						},
						onFailureWithBody: (statusCode, body) =>
						{
							return ProcessCreateFailure(statusCode, body,
								async (sfId) =>
								{
									if (!forceUpdate)
										return onSynchronized(sfId);

									var patchLocation = location.AppendToPath(sfId);
									return await patchLocation.HttpClientPatchDynamicAuthenticatedAsync(
											populateRequest: (request) =>
											{
												var content = new StringContent(json,
													encoding: System.Text.Encoding.UTF8,
													mediaType: "application/json");
												request.Content = content;
												return (request, () => { content.Dispose(); });
											},
											authToken: this.authToken, tokenType: this.tokenType,
										(Response response) =>
										{
											var id = sfId; // response will be null response.id;
											return onSynchronized(id);
										},
										onFailureWithBody: (statusCode, body) =>
										{
											return onFailure(statusCode, body);
										});
								},
								onFailure.AsAsyncFunc());
						},
							didTokenGetRefreshed: this.DidTokenGetRefreshed);
				},
				(code, why) =>
				{
					return onFailure(code, why).AsTask();
				});
		}

		private static string Serialize<TResource>(TResource resource,
			object[] extraResources,
			Dictionary<string, object> extraValues, Field[] fields)
		{
			var orignalType = typeof(TResource);
			var stringBuilder = new System.Text.StringBuilder();
			using (var textWriter = new System.IO.StringWriter(stringBuilder))
			{
				using (var jsonWriter = new JsonTextWriter(textWriter))
				{
					jsonWriter.WriteStartObject();

					SerializeObject(resource, jsonWriter, orignalType, fields);
					foreach (var extraRes in extraResources.NullToEmpty())
					{
						SerializeObject(extraRes, jsonWriter, orignalType, fields);
					}

					foreach (var extraValue in extraValues.NullToEmpty())
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

		private static JsonTextWriter SerializeObject(object resourceToSerialize, JsonTextWriter jsonWriter, Type originalType,
			Field [] fields)
		{
			var (matched, discard1, discard2) = resourceToSerialize
				.GetType()
				.GetPropertyAndFieldsWithAttributesInterface<IBindSalesforce>(multiple: true)
				.Match(fields,
					(memberAttrTpl, field) =>
					{
						var (member, attr) = memberAttrTpl;
						return attr.IsMatch(member, field, originalType);
					});

			foreach (var ((member, binder), field) in matched)
			{
				binder.PopluateSalesforceResource(jsonWriter, member, resourceToSerialize, field);
			}
			return jsonWriter;
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

			// [{"message":"Required fields are missing: [Name]","errorCode":"REQUIRED_FIELD_MISSING","fields":["Name"]}]
			REQUIRED_FIELD_MISSING,
		}

		internal class ErrorResponse
        {
			public string message;
			public ErrorCodes errorCode;
			public DuplicateResult duplicateResut;
			public string[] fields;
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

	}
}

