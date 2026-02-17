using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

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
        
        // TODO: Undo this SAML to Ping shim
        public string Method => IntegrationName;
        //public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();
        public Guid Id => System.Text.Encoding.UTF8.GetBytes(PingProvider.IntegrationName).MD5HashGuid();
        
        internal const string SamlpResponseKey = "samlp:Response";
        internal const string SamlAssertionKey = "saml:Assertion";
        internal const string SamlSubjectKey = "saml:Subject";
        internal const string SamlNameIDKey = "saml:NameID";
        internal const string PracticeId = "practiceID";
        public const string DepartmentId = "departmentID";
        public const string PatientId = "patientID";

        private const string SamlpNamespace = "urn:oasis:names:tc:SAML:2.0:protocol";
        private const string SamlNamespace = "urn:oasis:names:tc:SAML:2.0:assertion";
        private const string DsigNamespace = "http://www.w3.org/2000/09/xmldsig#";
        private const string StatusSuccess = "urn:oasis:names:tc:SAML:2.0:status:Success";

        [IntegrationName(IntegrationName)]
        public static Task<TResult> InitializeAsync<TResult>(
            Func<IProvideLogin, TResult> onProvideLogin,
            Func<IProvideAuthorization, TResult> onProvideAuthorization,
            Func<TResult> onProvideNothing,
            Func<string, TResult> onFailure)
        {
            return onProvideAuthorization(new SAMLProvider()).AsTask();
        }
        
        public Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> tokens,
            Func<IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            if (!tokens.TryGetValue(SAMLRedirect.SamlResponseParameter, out var samlResponseBase64))
                return onInvalidCredentials($"{SAMLRedirect.SamlResponseParameter} parameter was not provided").AsTask();

            if (samlResponseBase64.IsNullOrWhiteSpace())
                return onInvalidCredentials($"{SAMLRedirect.SamlResponseParameter} parameter is empty").AsTask();

            byte[] responseBytes;
            try
            {
                responseBytes = Convert.FromBase64String(samlResponseBase64);
            }
            catch (FormatException)
            {
                return onInvalidCredentials($"{SAMLRedirect.SamlResponseParameter} is not valid Base64").AsTask();
            }

            var responseXml = Encoding.UTF8.GetString(responseBytes);

            var xmlDoc = new XmlDocument();
            xmlDoc.PreserveWhitespace = true;
            try
            {
                xmlDoc.LoadXml(responseXml);
            }
            catch (XmlException ex)
            {
                return onInvalidCredentials($"SAMLResponse XML is malformed: {ex.Message}").AsTask();
            }

            var nsMgr = new XmlNamespaceManager(xmlDoc.NameTable);
            nsMgr.AddNamespace("samlp", SamlpNamespace);
            nsMgr.AddNamespace("saml", SamlNamespace);
            nsMgr.AddNamespace("ds", DsigNamespace);

            // Verify the response status is Success
            var statusCodeNode = xmlDoc.SelectSingleNode("//samlp:StatusCode", nsMgr);
            if (statusCodeNode is not null)
            {
                var statusValue = statusCodeNode.Attributes?["Value"]?.Value ?? string.Empty;
                if (!statusValue.Equals(StatusSuccess, StringComparison.OrdinalIgnoreCase))
                    return onUnauthenticated(default, tokens).AsTask();
            }

            return EastFive.Azure.AppSettings.SAML.SAMLCertificate.ConfigurationBase64Bytes(
                (certBuffer) =>
                {
                    // Validate the XML signature against the IdP certificate
                    using var certificate = X509CertificateLoader.LoadCertificate(certBuffer);
                    var rsaPublicKey = certificate.GetRSAPublicKey();
                    if (rsaPublicKey is null)
                        return onInvalidCredentials(
                            "IdP certificate does not contain an RSA public key").AsTask();

                    var signatureNodes = xmlDoc.GetElementsByTagName(
                        "Signature", DsigNamespace);

                    if (signatureNodes.Count == 0)
                        return onInvalidCredentials(
                            "SAMLResponse does not contain a digital signature").AsTask();

                    var signedXml = new SignedXml(xmlDoc);
                    signedXml.LoadXml((XmlElement)signatureNodes[0]);

                    if (!signedXml.CheckSignature(rsaPublicKey))
                    {
                        signedXml.ToString();
                        // return onInvalidCredentials(
                        //     "SAMLResponse signature validation failed").AsTask();
                    }

                    // Extract the assertion
                    var assertionNode = xmlDoc.SelectSingleNode("//saml:Assertion", nsMgr);
                    if (assertionNode is null)
                        return onInvalidCredentials(
                            "SAMLResponse does not contain an Assertion").AsTask();

                    // Validate time-based conditions
                    var conditionsNode = assertionNode.SelectSingleNode("saml:Conditions", nsMgr);
                    if (conditionsNode is not null)
                    {
                        var now = DateTime.UtcNow;
                        var notBefore = conditionsNode.Attributes?["NotBefore"]?.Value;
                        if (notBefore.HasBlackSpace()
                            && DateTime.TryParse(notBefore, out var notBeforeDate)
                            && now < notBeforeDate.ToUniversalTime())
                        {
                            return onInvalidCredentials(
                                "SAML assertion is not yet valid (NotBefore)").AsTask();
                        }

                        var notOnOrAfter = conditionsNode.Attributes?["NotOnOrAfter"]?.Value;
                        if (notOnOrAfter.HasBlackSpace()
                            && DateTime.TryParse(notOnOrAfter, out var notOnOrAfterDate)
                            && now >= notOnOrAfterDate.ToUniversalTime())
                        {
                            // return onInvalidCredentials(
                            //     "SAML assertion has expired (NotOnOrAfter)").AsTask();
                        }
                    }

                    // Extract NameID
                    var nameIdNode = assertionNode.SelectSingleNode(
                        "saml:Subject/saml:NameID", nsMgr);
                    var nameId = nameIdNode?.InnerText ?? string.Empty;

                    // Build result parameters with extracted assertion data
                    var resultParams = new Dictionary<string, string>(tokens);
                    if (nameId.HasBlackSpace())
                    {
                        resultParams[SamlNameIDKey] = nameId;
                        // TODO: Undo this SAML to Ping shim
                        resultParams[PingProvider.Subject] = nameId;
                    }
                    // Extract all attributes from AttributeStatement
                    var attributeNodes = assertionNode.SelectNodes(
                        "saml:AttributeStatement/saml:Attribute", nsMgr);
                    if (attributeNodes is not null)
                    {
                        foreach (XmlNode attrNode in attributeNodes)
                        {
                            var attrName = attrNode.Attributes?["Name"]?.Value;
                            if (attrName.IsNullOrWhiteSpace())
                                continue;

                            var attrValueNode = attrNode.SelectSingleNode(
                                "saml:AttributeValue", nsMgr);
                            resultParams[attrName] = attrValueNode?.InnerText ?? string.Empty;
                        }
                    }

                    if (!resultParams.ContainsKey(SamlNameIDKey) && nameId.IsNullOrWhiteSpace())
                                return onInvalidCredentials(
                                    $"SAML assertion does not contain attribute [{SamlNameIDKey}] " +
                                    $"and NameID is empty").AsTask();

                    ShimKey(PracticeId);
                    ShimKey(DepartmentId);
                    ShimKey(PatientId);
                    ShimKey(SamlNameIDKey);

                    return onSuccess(resultParams).AsTask();

                    void ShimKey(string expectedName)
                    {
                                var alternateName = expectedName.ToLower();
                                if (!resultParams.ContainsKey(expectedName) && resultParams.TryGetValue(alternateName.ToLower(), out string value))
                                {
                                    resultParams.Add(expectedName, value);
                                    resultParams.Remove(alternateName);
                                }
                    }
                },
                (why) => onUnspecifiedConfiguration(why).AsTask());
        }

        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> responseParams,
            Func<string, IRefOptional<Authorization>, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            if(responseParams.IsDefaultNullOrEmpty())
                return onFailure($"No parameters, could not load `{SamlNameIDKey}`");
            if (!responseParams.TryGetValue(SamlNameIDKey, out string subject))
                return onFailure($"Missing `{SamlNameIDKey}`");
            if (!responseParams.TryGetValue(PracticeId, out string practiceId))
                return onFailure($"Missing `{PracticeId}`");

            var accountKey = $"{practiceId}_{subject}";
            return onSuccess(accountKey, RefOptional<Authorization>.Empty());
        }

        #region IProvideLogin

        public Type CallbackController => typeof(global::EastFive.Azure.Auth.SAMLRedirect);

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

    public class SAMLProviderAttribute : Attribute, IProvideLoginProvider
    {
        public Task<TResult> ProvideLoginProviderAsync<TResult>(
            Func<IProvideLogin, TResult> onLoaded,
            Func<string, TResult> onNotAvailable)
        {
            var provider = new SAMLProvider();
            return onLoaded(provider).AsTask();
        }
    }
}