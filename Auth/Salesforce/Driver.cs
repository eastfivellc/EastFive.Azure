using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Net;
using EastFive.Serialization.Json;
using EastFive.Web.Configuration;
using Newtonsoft.Json;

namespace EastFive.Azure.Auth.Salesforce
{
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


		public class Leads
        {
			private Driver driver;

			public string Salutation;
			public string FirstName;
			public string LastName;
			public string Company;
			public string Title;
			public string Email;

			/// <summary>
			/// Must contain <code>Name</code> property
			/// </summary>
			public object RecordType;
			public string RecordTypeId;

			public Leads(Driver driver)
            {
				this.driver = driver;
            }

			public async Task<TResult> CreateAsync<TResult>(
				Func<string, TResult> onCreated,
				Func<HttpStatusCode, string, TResult> onFailure = default)
			{
				Uri.TryCreate($"{this.driver.instanceUrl}/services/data/v53.0/sobjects/Lead/", UriKind.Absolute, out Uri leadLocation);

				return await leadLocation.HttpClientPostResourceAsync(this,
						authToken:this.driver.authToken, tokenType:this.driver.tokenType,
					(string response) => onCreated(response),
					onFailureWithBody: (statusCode, body) =>
					{
						if (onFailure.IsDefaultOrNull())
							throw new Exception($"[{statusCode}]:{body}");
						return onFailure(statusCode, body);
					},
						didTokenGetRefreshed:async (statusCode, body) =>
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

											return await driver.RefreshToken(
												onRefreshed:(newToken) =>
                                                {
													return (true, newToken);
												},
												onFailure:(why) => (false, string.Empty));
										},
										() => (false, string.Empty).AsTask());
								},
								(message) => (false, string.Empty).AsTask());
							
						});

				//return await leadLocation.HttpClientPostResourceAsync(this,
				//	(string response) => onCreated(response),
				//	onFailureWithBody: (statusCode, body) =>
    //                {
				//		if (onFailure.IsDefaultOrNull())
				//			throw new Exception($"[{statusCode}]:{body}");
				//		return onFailure(statusCode, body);
    //                },
				//	mutateRequest:
				//		request =>
				//		{
				//			request.Headers.Add("Authorization", $"{this.driver.tokenType} {this.driver.authToken}");
				//			return request;
				//		});
			}
		}

		public class Accounts
		{
			private Driver driver;

			public string Name;

			public string Type;

			public string AccountSource;

			public string Description;

			[JsonProperty(PropertyName = "EMR__c")]
			public string EMR;

			public string ParentId;

			[JsonProperty(PropertyName = "City__c")]
			public string City;

			[JsonProperty(PropertyName = "State__c")]
			public string State;

			[JsonProperty(PropertyName = "Street_Address__c")]
			public string StreetAddress;

			[JsonProperty(PropertyName = "Zip__c")]
			public string Zip;

			public Accounts(Driver driver)
			{
				this.driver = driver;
			}

			public async Task<TResult> CreateAsync<TResult>(
				Func<string, TResult> onCreated,
				Func<HttpStatusCode, string, TResult> onFailure = default)
			{
				Uri.TryCreate($"{this.driver.instanceUrl}/services/data/v54.0/sobjects/Account/", UriKind.Absolute, out Uri leadLocation);

				return await leadLocation.HttpClientPostResourceAsync(this,
						authToken: this.driver.authToken, tokenType: this.driver.tokenType,
					(Response response) =>
					{
						var id = response.id;
						return onCreated(id);
					},
					onFailureWithBody: (statusCode, body) =>
					{
						return ProcessFailure(statusCode, body, onCreated, onFailure);
					},
						didTokenGetRefreshed: driver.DidTokenGetRefreshed);
			}
		}

		protected static TResult ProcessFailure<TResult>(HttpStatusCode statusCode, string body,
			Func<string, TResult> onCreated,
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

													return onCreated(dupMatch.record.Id);
												},
												() => OnFailure());
									},
									() => OnFailure());

						},
						() => OnFailure());
				},
				(message) => onFailure(statusCode, message));

			TResult OnFailure()
			{
				if (onFailure.IsDefaultOrNull())
					throw new Exception($"[{statusCode}]:{body}");
				return onFailure(statusCode, body);
			}
		}

		public class Contacts
		{
			private Driver driver;

			public string Salutation;
			public string FirstName;
			public string LastName;
			// public string Company;
			public string Title;
			public string Email;

			[JsonProperty(PropertyName = "AccountId")]
			public string Account;


			/// <summary>
			/// Must contain <code>Name</code> property
			/// </summary>
			public object RecordType;
			public string RecordTypeId;

			public Contacts(Driver driver)
			{
				this.driver = driver;
			}

			public async Task<TResult> CreateAsync<TResult>(
				Func<string, TResult> onCreated,
				Func<HttpStatusCode, string, TResult> onFailure = default)
			{
				Uri.TryCreate($"{this.driver.instanceUrl}/services/data/v54.0/sobjects/Contact/", UriKind.Absolute, out Uri leadLocation);

				return await leadLocation.HttpClientPostResourceAsync(this,
						authToken: this.driver.authToken, tokenType: this.driver.tokenType,
					(Response response) =>
					{
						var id = response.id;
						return onCreated(id);
					},
					onFailureWithBody: (statusCode, body) =>
					{
						return ProcessFailure(statusCode, body, onCreated, onFailure);
					},
						didTokenGetRefreshed: driver.DidTokenGetRefreshed);
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

		public class Logins
		{
			private Driver driver;

			[JsonProperty(PropertyName = "Login_Date__c")]
			public DateTime When;

			[JsonProperty(PropertyName = "Login_User__c")]
			public string User;

			[JsonProperty(PropertyName = "LoginDayIndex__c")]
			public string LoginDayIndex => $"{When.Year}|{When.DayOfYear}";

			public Logins(Driver driver)
			{
				this.driver = driver;
			}

			public async Task<TResult> CreateAsync<TResult>(
				Func<string, TResult> onCreated,
				Func<HttpStatusCode, string, TResult> onFailure = default)
			{
				Uri.TryCreate($"{this.driver.instanceUrl}/services/data/v54.0/sobjects/Logins__c/", UriKind.Absolute, out Uri leadLocation);

				return await leadLocation.HttpClientPostResourceAsync(this,
						authToken: this.driver.authToken, tokenType: this.driver.tokenType,
					(Response response) =>
					{
						var id = response.id;
						return onCreated(id);
					},
					onFailureWithBody: (statusCode, body) =>
					{
						return ProcessFailure(statusCode, body, onCreated, onFailure);
					},
						didTokenGetRefreshed: driver.DidTokenGetRefreshed);

			}
		}

		public enum ErrorCodes
        {
			INVALID_SESSION_ID,
			DUPLICATES_DETECTED,
		}

		internal class ErrorResponse
        {
			public string message;
			public ErrorCodes errorCode;
			public DuplicateResult duplicateResut;
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
	}
}

