using BlackBarLabs.Extensions;
using EastFive.Security.CredentialProvider;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using EastFive.Security.SessionServer.Persistence;
using EastFive.Api.Services;
using System.Security.Claims;
using EastFive.Security.SessionServer;
using EastFive.Api.Azure.Credentials.Attributes;
using EastFive.Serialization;
using EastFive.Web.Configuration;

namespace EastFive.Api.Azure.Credentials
{
    [IntegrationName(IntegrationName)]
    public class SAMLProvider : IProvideLogin
    {
        public const string IntegrationName = "SAML";
        public string Method => IntegrationName;
        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

        private DataContext dataContext;
        
        internal const string SamlpResponseKey = "samlp:Response";
        internal const string SamlAssertionKey = "saml:Assertion";
        internal const string SamlSubjectKey = "saml:Subject";
        internal const string SamlNameIDKey = "saml:NameID";

        public SAMLProvider()
        {
            this.dataContext = new DataContext(EastFive.Azure.AppSettings.ASTConnectionStringKey);
        }

        [IntegrationName(IntegrationName)]
        public static Task<TResult> InitializeAsync<TResult>(
            Func<IProvideLogin, TResult> onProvideLogin,
            Func<IProvideAuthorization, TResult> onProvideAuthorization,
            Func<TResult> onProvideNothing,
            Func<string, TResult> onFailure)
        {
            return onProvideAuthorization(new SAMLProvider()).ToTask();
        }
        
        public async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> tokens,
            Func<string, Guid?, Guid?, IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            return await EastFive.Azure.AppSettings.SAML.SAMLCertificate.ConfigurationBase64Bytes(
                async (certBuffer) =>
                {
                    var certificate = new X509Certificate2(certBuffer);
                    var m = ((RSACryptoServiceProvider)certificate.PrivateKey);
                    AsymmetricAlgorithm trustedSigner = m; // AsymmetricAlgorithm.Create(certificate.GetKeyAlgorithm()
                    var trustedSigners = default(AsymmetricAlgorithm) == trustedSigner ? null : trustedSigner.AsEnumerable();
                    
                    try
                    {
                        var nameId = tokens[SAMLProvider.SamlNameIDKey];

                        return EastFive.Web.Configuration.Settings.GetString(EastFive.Azure.AppSettings.SAML.SAMLLoginIdAttributeName,
                            (attributeName) =>
                            {
                                //var attributes = assertion.Attributes
                                //    .Where(attribute => attribute.Name.CompareTo(attributeName) == 0)
                                //    .ToArray();
                                //if (attributes.Length == 0)
                                //    return invalidCredentials($"SAML assertion does not contain an attribute with name [{attributeName}] which is necessary to operate with this system");
                                //Guid authId;
                                //if (!Guid.TryParse(attributes[0].AttributeValue.First(), out authId))
                                //    return invalidCredentials("User's auth identifier is not a guid.");

                                var hash = SHA512.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(nameId));
                                var loginId = new Guid(hash.Take(16).ToArray());

                                return onSuccess(nameId, default(Guid?), loginId, tokens);
                            },
                            (why) => onUnspecifiedConfiguration(why));
                    } catch(Exception ex)
                    {
                        return await onInvalidCredentials("SAML Assertion parse and validate failed").ToTask();
                    }
                },
                (why) => onUnspecifiedConfiguration(why).ToTask());
        }

        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> tokens,
            Func<string, Guid?, Guid?, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {

            var nameId = tokens[SAMLProvider.SamlNameIDKey];
            var hash = SHA512.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(nameId));
            var loginId = new Guid(hash.Take(16).ToArray());

            return onSuccess(nameId, default(Guid?), loginId);
        }


        #region IProvideLogin

        public Type CallbackController => typeof(SAMLProvider); // typeof(Controllers.SAMLRedirectController);

        public Uri GetSignupUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            throw new NotImplementedException();
        }

        public Uri GetLogoutUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            throw new NotImplementedException();
        }

        public Uri GetLoginUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> UserParametersAsync<TResult>(Guid actorId, System.Security.Claims.Claim[] claims, IDictionary<string, string> extraParams, Func<IDictionary<string, string>, IDictionary<string, Type>, IDictionary<string, string>, TResult> onSuccess)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
