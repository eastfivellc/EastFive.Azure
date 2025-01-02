using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive.Api.Services;
using EastFive.Serialization;
using EastFive.Web.Configuration;
using EastFive.Azure.Auth;
using EastFive.Extensions;

namespace EastFive.Azure.Auth.CredentialProviders
{
    [IntegrationName(IntegrationName)]
    public class SAMLProvider : IProvideLogin
    {
        public const string IntegrationName = "SAML";
        public string Method => IntegrationName;
        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();
        
        internal const string SamlpResponseKey = "samlp:Response";
        internal const string SamlAssertionKey = "saml:Assertion";
        internal const string SamlSubjectKey = "saml:Subject";
        internal const string SamlNameIDKey = "saml:NameID";

        [IntegrationName(IntegrationName)]
        public static Task<TResult> InitializeAsync<TResult>(
            Func<IProvideLogin, TResult> onProvideLogin,
            Func<IProvideAuthorization, TResult> onProvideAuthorization,
            Func<TResult> onProvideNothing,
            Func<string, TResult> onFailure)
        {
            return onProvideAuthorization(new SAMLProvider()).AsTask();
        }
        
        public async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> tokens,
            Func<IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            return await EastFive.Azure.AppSettings.SAML.SAMLCertificate.ConfigurationBase64Bytes(
                async (certBuffer) =>
                {   
                    using (var certificate = X509CertificateLoader.LoadCertificate(certBuffer))
                    {
                        var m = certificate.GetRSAPrivateKey();
                        AsymmetricAlgorithm trustedSigner = m; // AsymmetricAlgorithm.Create(certificate.GetKeyAlgorithm()
                        var trustedSigners = default(AsymmetricAlgorithm) == trustedSigner ? null : trustedSigner.AsEnumerable();
                    }
                    try
                    {
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

                                return onSuccess(tokens);
                            },
                            (why) => onUnspecifiedConfiguration(why));
                    } catch(Exception)
                    {
                        return await onInvalidCredentials("SAML Assertion parse and validate failed").AsTask();
                    }
                },
                (why) => onUnspecifiedConfiguration(why).AsTask());
        }

        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> responseParams,
            Func<string, IRefOptional<Authorization>, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            var nameId = responseParams[SAMLProvider.SamlNameIDKey];

            return onSuccess(nameId, GetState());

            IRefOptional<Authorization> GetState()
            {
                if (!responseParams.TryGetValue("responseParamState", out string stateValue))
                    return RefOptional<Authorization>.Empty();

                RefOptional<Authorization>.TryParse(stateValue, out IRefOptional<Authorization> stateId);
                return stateId;
            }
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
