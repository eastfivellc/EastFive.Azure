using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using EastFive.Serialization;
using EastFive.Extensions;
using System.Net.Http;

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
        public const string PracticeId = "practiceID";
        public const string EncounterId = "Encounter id";
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
        
        public async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> tokens,
            Func<IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            if (!tokens.TryGetValue(SAMLRedirect.SamlResponseParameter, out var samlResponseBase64))
                return onInvalidCredentials($"{SAMLRedirect.SamlResponseParameter} parameter was not provided");

            if (samlResponseBase64.IsNullOrWhiteSpace())
                return onInvalidCredentials($"{SAMLRedirect.SamlResponseParameter} parameter is empty");

            if (!tokens.TryGetValue(SAMLRedirect.MetadataLocationParameter, out var metadataLocationStr) || !Uri.TryCreate(metadataLocationStr, UriKind.Absolute, out var metadataLocation))
                return onUnspecifiedConfiguration($"{SAMLRedirect.MetadataLocationParameter} was not configured");

            byte[] responseBytes;
            try
            {
                responseBytes = Convert.FromBase64String(samlResponseBase64);
            }
            catch (FormatException)
            {
                return onInvalidCredentials($"{SAMLRedirect.SamlResponseParameter} is not valid Base64");
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
                return onInvalidCredentials($"SAMLResponse XML is malformed: {ex.Message}");
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
                    return onUnauthenticated(default, tokens);
            }

            return await FetchIdPCertificateFromMetadataAsync(metadataLocation,
                (certBuffer) =>
                {
                    // Validate the XML signature against the IdP certificate
                    using var certificate = X509CertificateLoader.LoadCertificate(certBuffer);
                    var rsaPublicKey = certificate.GetRSAPublicKey();
                    if (rsaPublicKey is null)
                        return onInvalidCredentials(
                            "IdP certificate does not contain an RSA public key");

                    var signatureNodes = xmlDoc.GetElementsByTagName(
                        "Signature", DsigNamespace);

                    if (signatureNodes.Count == 0)
                        return onInvalidCredentials(
                            "SAMLResponse does not contain a digital signature");

                    // Register ID attributes so SignedXml can resolve
                    // the Reference URI (SAML uses "ID" which is not
                    // recognised by XmlDocument.GetElementById without this).
                    var signedXml = new SamlSignedXml(xmlDoc);
                    signedXml.LoadXml((XmlElement)signatureNodes[0]);

                    if (!signedXml.CheckSignature(rsaPublicKey))
                    {
                        return onInvalidCredentials(
                            "SAMLResponse signature validation failed");
                    }

                    // Extract the assertion
                    var assertionNode = xmlDoc.SelectSingleNode("//saml:Assertion", nsMgr);
                    if (assertionNode is null)
                        return onInvalidCredentials(
                            "SAMLResponse does not contain an Assertion");

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
                                "SAML assertion is not yet valid (NotBefore)");
                        }

                        var notOnOrAfter = conditionsNode.Attributes?["NotOnOrAfter"]?.Value;
                        if (notOnOrAfter.HasBlackSpace()
                            && DateTime.TryParse(notOnOrAfter, out var notOnOrAfterDate)
                            && now >= notOnOrAfterDate.ToUniversalTime())
                        {
                            return onInvalidCredentials(
                                "SAML assertion has expired (NotOnOrAfter)");
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

                            var attrValue = attrValueNode?.InnerText ?? string.Empty;
                            // filter out invalid values
                            if (attrValue.HasBlackSpace() && attrValue.Equals(attrName, StringComparison.OrdinalIgnoreCase))
                                attrValue = string.Empty;
                            resultParams[attrName] = attrValue;
                        }
                    }

                    if (!resultParams.ContainsKey(SamlNameIDKey) && nameId.IsNullOrWhiteSpace())
                        return onInvalidCredentials(
                            $"SAML assertion does not contain attribute [{SamlNameIDKey}] " +
                            $"and NameID is empty");

                    ShimKey(PracticeId);
                    ShimKey(EncounterId);
                    ShimKey(DepartmentId);
                    ShimKey(PatientId);
                    ShimKey(SamlNameIDKey);

                    return onSuccess(resultParams);

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
                (why) => onFailure(why));
        }

        private static async Task<TResult> FetchIdPCertificateFromMetadataAsync<TResult>(
            Uri metadataUri,
            Func<byte[], TResult> onFound,
            Func<string, TResult> onFailure)
        {
            string metadataXml;
            try
            {
                using var httpClient = new HttpClient();
                metadataXml = await httpClient.GetStringAsync(metadataUri);
            }
            catch (Exception ex)
            {
                return onFailure($"Failed to fetch IdP metadata from {metadataUri.AbsoluteUri}: {ex.Message}");
            }

            var metadataDoc = new XmlDocument();
            try
            {
                metadataDoc.LoadXml(metadataXml);
            }
            catch (XmlException ex)
            {
                return onFailure($"IdP metadata XML is malformed: {ex.Message}");
            }

            var metadataNsMgr = new XmlNamespaceManager(metadataDoc.NameTable);
            metadataNsMgr.AddNamespace("md", "urn:oasis:names:tc:SAML:2.0:metadata");
            metadataNsMgr.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");

            // Extract the signing certificate from the metadata
            // Look for KeyDescriptor with use="signing" first, fall back to any X509Certificate
            var certNode = metadataDoc.SelectSingleNode(
                "//md:KeyDescriptor[@use='signing']/ds:KeyInfo/ds:X509Data/ds:X509Certificate",
                metadataNsMgr);

            certNode ??= metadataDoc.SelectSingleNode(
                "//ds:X509Data/ds:X509Certificate",
                metadataNsMgr);

            if (certNode is null)
                return onFailure("IdP metadata does not contain a ds:X509Certificate element");

            var certBase64 = certNode.InnerText.Trim().Replace("\n", string.Empty);
            if (certBase64.IsNullOrWhiteSpace())
                return onFailure("ds:X509Certificate element in IdP metadata is empty");

            byte[] certBytes;
            try
            {
                certBytes = Convert.FromBase64String(certBase64);
            }
            catch (FormatException)
            {
                return onFailure("ds:X509Certificate in IdP metadata is not valid Base64");
            }

            return onFound(certBytes);
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

        /// <summary>
        /// SignedXml subclass that resolves SAML's "ID" attribute for
        /// Reference URI lookups. The base class only recognises "id",
        /// "Id", or DTD-declared ID attributes, so SAML signatures
        /// that reference elements by their "ID" attribute fail without this.
        /// </summary>
        private sealed class SamlSignedXml : SignedXml
        {
            public SamlSignedXml(XmlDocument doc) : base(doc) { }

            public override XmlElement GetIdElement(XmlDocument document, string idValue)
            {
                var element = base.GetIdElement(document, idValue);
                if (element is not null)
                    return element;

                // Fall back to searching for elements whose "ID" attribute
                // matches the reference value (case-sensitive).
                return FindById(document.DocumentElement, idValue);
            }

            private static XmlElement FindById(XmlElement root, string idValue)
            {
                if (root is null)
                    return null;

                if (root.GetAttribute("ID") == idValue)
                    return root;

                foreach (XmlNode child in root.ChildNodes)
                {
                    if (child is XmlElement childElement)
                    {
                        var found = FindById(childElement, idValue);
                        if (found is not null)
                            return found;
                    }
                }
                return null;
            }
        }
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