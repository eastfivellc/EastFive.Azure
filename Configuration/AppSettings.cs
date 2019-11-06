using EastFive.Web;

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

        [ConfigKey("Identifies this application to an AADB2C application",
            DeploymentOverrides.Suggested,
            DeploymentSecurityConcern = false,
            Location = "Azure Portal | Azure Active Directory | App Registrations | Application ID",
            PrivateRepositoryOnly = false)]
        public const string AADB2CAudience = "EastFive.Security.LoginProvider.AzureADB2C.Audience";
        public const string AADB2CSigninConfiguration = "EastFive.Security.LoginProvider.AzureADB2C.SigninEndpoint";
        public const string AADB2CSignupConfiguration = "EastFive.Security.LoginProvider.AzureADB2C.SignupEndpoint";

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
