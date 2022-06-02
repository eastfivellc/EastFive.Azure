using System;


namespace EastFive.Azure.Auth.Salesforce
{
	public class SalesforceApiPathAttribute : System.Attribute, IDefineSalesforceApiPath
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
}

