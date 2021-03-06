﻿using EastFive.Web;

namespace EastFive.Azure
{
    [Config]
    public static class AppSettings
    {
        [ConfigKey("Connection string used by default for AzureStorageTables.",
           DeploymentOverrides.Suggested,
           DeploymentSecurityConcern = false,
           Location = "Azure Portal | Storage | Connection Strings",
           PrivateRepositoryOnly = true)]
        public const string ASTConnectionStringKey = "EastFive.Azure.StorageTables.ConnectionString";
        public const string TableInformationToken = "EastFive.Azure.StorageTables.TableInformationToken";

        public static class ApplicationInsights
        {
            [ConfigKey("Identifies the application insights endpoint to which data is posted.",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = false,
                Location = "Home > Application Insights > {Resource Name} > Dashboard / Instrumentation Key")]
            public const string InstrumentationKey = "EastFive.Azure.ApplicationInsights.InstrumentationKey";

            [ConfigKey("Username for API access to Application Insights.",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = true,
                Location = "Home > Application Insights > {Resource Name} > API Access / Application ID")]
            public const string ApplicationId = "EastFive.Azure.ApplicationInsights.ApplicationId";

            [ConfigKey("Password for API access to Application Insights.",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = true,
                Location = "Home > Application Insights > {Resource Name} > API Access / + " +
                "Create API Key")]
            public const string ClientSecret = "EastFive.Azure.ApplicationInsights.ClientSecret";

            public const string TeamsHook = "EastFive.Azure.ApplicationInsights.TeamsHook";

            public const string TeamsAppIdentification = "EastFive.Azure.ApplicationInsights.TeamsAppIdentification";

            public const string TeamsAppImage = "EastFive.Azure.ApplicationInsights.TeamsAppImage";
        }

        public const string ApiSecurityKey = "EastFive.Security.SessionServer.ApiSecurityKey";

        public const string SpaSiteLocation = "EastFive.Azure.SpaSiteLocation";

        public const string AdminLoginRsaKey = "EastFive.Azure.Auth.AdminLoginRsaKey";
        public const string ClientMinimumVersion = "EastFive.Azure.Modules.ClientMinimumVersion";

        public const string FunctionProcessorServiceBusTriggerName = "EastFive.Azure.Functions.ServiceBusTriggerName";
        public const string FunctionProcessorServiceBusTriggerNamePercent = "%" + FunctionProcessorServiceBusTriggerName + "%";

        public const string CDNEndpointHostname = "EastFive.Azure.CDNEndpointHostname";
        public const string CDNApiRoutePrefix = "EastFive.Azure.CDNApiRoutePrefix";

        [Config]
        public static class CognitiveServices
        {
            [ConfigKey("Endpoint used by computer vision to analyze image Content.",
                DeploymentOverrides.Suggested,
                Location = "Azure portal image classification quick start",
                DeploymentSecurityConcern = false)]
            public const string ComputerVisionEndpoint = "EastFive.Azure.CognitiveServices.ComputerVisionEndpoint";

            [ConfigKey("Subscription key that provides access to the computer vision API.",
                DeploymentOverrides.Suggested,
                Location = "Azure portal image classification quick start",
                DeploymentSecurityConcern = false)]
            public const string ComputerVisionSubscriptionKey = "EastFive.Azure.CognitiveServices.ComputerVisionSubscriptionKey";
        }

        [Config]
        public static class SAML
        {
            [ConfigKey("The certificate the SAML provider offers. It is in base64 format. Only the public key is availble. " +
                "It is used to verfiy the signature of the SAML assurtion.",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = false)]
            public const string SAMLCertificate = "EastFive.Security.CredentialProvider.SAML.Certificate";
            
            [ConfigKey("The name of the attribute in the SAML assertion whoms value contains a unique key identifying the user. " + 
                "This value is used to lookup the user in the local system.",
                DeploymentOverrides.Optional,
                DeploymentSecurityConcern = false)]
            public const string SAMLLoginIdAttributeName = "EastFive.Security.CredentialProvider.SAML.LoginIdAttributeName";
        }

        [Config]
        public static class Apple
        {
            [ConfigKey("Used to prove ownership of this domain to Apple.",
                DeploymentOverrides.Optional,
                Location = "Apple developer portal -> Login with apple",
                DeploymentSecurityConcern = false)]
            public const string DeveloperSiteAssociation = "EastFive.Azure.Apple.DeveloperSiteAssociation";
	}

        public static class AzureADB2C
        {
            [ConfigKey("Identifies this Tenant to AADB2C",
                DeploymentOverrides.Mandatory,
                DeploymentSecurityConcern = false,
                Location = "Azure Portal | Azure Active Directory B2C | [*].onmicrosoft.com",
                PrivateRepositoryOnly = false)]
            public const string Tenant = "EastFive.Azure.AzureADB2C.Tenant";

            [ConfigKey("Identifies this application (multiple applications per tenant) to an AADB2C application",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = false,
                Location = "Azure Portal | Azure Active Directory | App Registrations | Application ID",
                PrivateRepositoryOnly = false)]
            public const string ApplicationId = "EastFive.Azure.AzureADB2C.ApplicationId";
            
            [ConfigKey("The audience used for token validation.",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = false,
                Location = "Capture a valid response JWT and check",
                PrivateRepositoryOnly = false)]
            public const string Audience = "EastFive.Azure.AzureADB2C.Audience";

            [ConfigKey("The name of the flow for user sign in.",
                DeploymentOverrides.Optional,
                DeploymentSecurityConcern = false,
                PrivateRepositoryOnly = false,
                Location = "Azure Portal | Azure Active Directory B2C | UserFlows")]
            public const string SigninFlow = "EastFive.Azure.AzureADB2C.SigninFlow";

            [ConfigKey("The name of the flow for user sign up.",
                DeploymentOverrides.Optional,
                DeploymentSecurityConcern = false,
                PrivateRepositoryOnly = false,
                Location = "Azure Portal | Azure Active Directory B2C | UserFlows")]
            public const string SignupFlow = "EastFive.Azure.AzureADB2C.SignupFlow";

            [ConfigKey("ID of the client", DeploymentOverrides.Mandatory)]
            public const string ClientId = "EastFive.Azure.AzureADB2C.ClientId";

            [ConfigKey("ID of the client", DeploymentOverrides.Mandatory,
                Location = "AD UI -> App Registrations -> [APP] -> All Settings -> Keys -> [New Key]",
                MoreInfo = "This can only be accessed when the Key is created")]
            public const string ClientSecret = "EastFive.Azure.AzureADB2C.ClientSecret";
        }

        public const string Redirections = "EastFive.Azure.Auth.Redirections";
        public const string PauseRedirections = "EastFive.Azure.Auth.PauseRedirections";

        [ConfigKey("Expiration in days for immutable spa files.",
            DeploymentOverrides.Optional,
            DeploymentSecurityConcern = false,
            Location = "Discressionary")]
        public const string SpaFilesExpirationInDays = "EastFive.Azure.SpaFilesExpirationInDays";

        [ConfigKey("Enable dynamic serving of the SPA.",
            DeploymentOverrides.Optional,
            DeploymentSecurityConcern = false,
            Location = "Discressionary")]
        public const string SpaServeEnabled = "EastFive.Azure.SpaServeEnabled";
    }
}

namespace EastFive.Security.SessionServer.Configuration
{
    [Config]
    public static class AppSettings
    {
        //public const string Storage = "EastFive.Security.SessionServer.Storage";
        public const string TokenExpirationInMinutes = "EastFive.Security.SessionServer.tokenExpirationInMinutes";
        public const string LoginIdClaimType = "EastFive.Security.SessionServer.LoginProvider.LoginIdClaimType";

        public const string PingIdentityAthenaRestApiKey = "EastFive.Security.LoginProvider.PingIdentity.Athena.RestApiKey";
        public const string PingIdentityAthenaRestAuthUsername = "EastFive.Security.LoginProvider.PingIdentity.Athena.RestAuthUsername";
        
        [ConfigKey("Link that is sent (emailed) to the user to login to the application",
            DeploymentOverrides.Desireable,
            DeploymentSecurityConcern = false,
            Location = "The URL that the webUI is deployed")]
        public const string LandingPage = "EastFive.Security.SessionServer.RouteDefinitions.LandingPage";
        public const string AppleAppSiteAssociationId = "EastFive.Security.SessionServer.AppleAppSiteAssociation.AppId";

        [ConfigKey("Connection string that is used for the service bus.",
            DeploymentOverrides.Suggested,
            DeploymentSecurityConcern = true,
            PrivateRepositoryOnly = true,
            Location = "The URL that the webUI is deployed")]
        public const string ServiceBusConnectionString = "EastFive.Api.Workers.ServiceBusConnectionString";

        

        public static class TokenCredential
        {
            /// <summary>
            /// The email address and name from which a token credential is sent.
            /// </summary>
            public const string FromEmail = "EastFive.Security.SessionServer.TokenCredential.FromEmail";
            public const string FromName = "EastFive.Security.SessionServer.TokenCredential.FromName";
            /// <summary>
            /// Subject for token credntial email.
            /// </summary>
            public const string Subject = "EastFive.Security.SessionServer.TokenCredential.Subject";
        }
    }
}
