using System;
using System.Linq;
using System.Xml.Linq;

using EastFive.Api;
using EastFive.Azure.Auth.CredentialProviders;
using EastFive.Extensions;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Auth
{
    [FunctionViewController(
        Namespace = ".well-known",
        Route = "saml-metadata",
        ContentType = "application/samlmetadata+xml",
        ContentTypeVersion = "2.0")]
    public class SAMLMetadata
    {
        [Unsecured("SAML service provider metadata - publicly accessible for IdP configuration")]
        [HttpGet(MatchAllParameters = false)]
        public static IHttpResponse Get(
                [QueryParameter(Name = "tag")]string tag,
                RequestMessage<SAMLRedirect> samlRedirectQuery,
                RequestMessage<SAMLMetadata> samlMetadataQuery,
            TextResponse onResponse,
            ConfigurationFailureResponse onConfigurationFailure)
        {
            return SamlMetadataBuilder.BuildMetadataXml(
                    tag,
                    samlRedirectQuery,
                    samlMetadataQuery,
                (xml) => onResponse(xml, System.Text.Encoding.UTF8, filename: $"saml-metadata-affirmhealth-{tag}.xml", contentType: "application/samlmetadata+xml"),
                (why) => onConfigurationFailure(why, why));
        }
    }

    internal static class SamlMetadataBuilder
    {
        private const string ProtocolSupport = "urn:oasis:names:tc:SAML:2.0:protocol";
        private const string BindingHttpPost = "urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST";
        private const string BindingHttpRedirect = "urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect";
        private const string DefaultNameIdFormat = "urn:oasis:names:tc:SAML:2.0:nameid-format:persistent";

        public static IHttpResponse BuildMetadataXml(
                string tag,
                IQueryable<SAMLRedirect> samlRedirectQuery,
                IQueryable<SAMLMetadata> samlMetadataQuery,
            Func<string, IHttpResponse> onSuccess,
            Func<string, IHttpResponse> onFailure)
        {
            var entityId = samlMetadataQuery
                            .ById(tag)
                            .CompileRequest()
                            .RequestUri
                            .AbsoluteUri;
            var acsUri = samlRedirectQuery
                            .ById(tag)
                            .CompileRequest()
                            .RequestUri;
            var sloUri = samlRedirectQuery
                            .HttpAction("logout")
                            .ById(tag)
                            .CompileRequest()
                            .RequestUri;
            var nameIdFormat = GetOptionalString(AppSettings.SAML.NameIdFormat, DefaultNameIdFormat);
            var signingCertBase64 = GetOptionalCertificateBase64(AppSettings.SAML.ServiceProviderCertificate);
            var xml = BuildMetadataDocument(entityId, acsUri, sloUri, nameIdFormat, signingCertBase64);
            return onSuccess(xml);
        }

        private static string BuildMetadataDocument(string entityId, Uri acsUri, Uri sloUri,
            string nameIdFormat, string signingCertBase64)
        {
            var md = XNamespace.Get("urn:oasis:names:tc:SAML:2.0:metadata");
            var ds = XNamespace.Get("http://www.w3.org/2000/09/xmldsig#");

            var keyDescriptor = signingCertBase64.IsNullOrWhiteSpace()
                ? default(XElement)
                : new XElement(md + "KeyDescriptor",
                    new XAttribute("use", "signing"),
                    new XElement(ds + "KeyInfo",
                        new XElement(ds + "X509Data",
                            new XElement(ds + "X509Certificate", signingCertBase64))));

            var sloService = sloUri == default(Uri)
                ? default(XElement)
                : new XElement(md + "SingleLogoutService",
                    new XAttribute("Binding", BindingHttpRedirect),
                    new XAttribute("Location", sloUri.AbsoluteUri));

            var spSsoDescriptor = new XElement(md + "SPSSODescriptor",
                new XAttribute("protocolSupportEnumeration", ProtocolSupport),
                new XAttribute("AuthnRequestsSigned", "false"),
                new XAttribute("WantAssertionsSigned", "true"),
                new XElement[]
                {
                    keyDescriptor,
                    sloService,
                    new XElement(md + "NameIDFormat", nameIdFormat),
                    new XElement(md + "AssertionConsumerService",
                        new XAttribute("Binding", BindingHttpPost),
                        new XAttribute("Location", acsUri.AbsoluteUri),
                        new XAttribute("index", "0"),
                        new XAttribute("isDefault", "true")),
                }
                .Where(element => element != null));

            var entityDescriptor = new XElement(md + "EntityDescriptor",
                new XAttribute("entityID", entityId),
                new XAttribute(XNamespace.Xmlns + "ds", ds),
                spSsoDescriptor);

            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                entityDescriptor);

            return document.ToString(SaveOptions.DisableFormatting);
        }

        private static string GetOptionalString(string key, string defaultValue)
        {
            return Settings.GetString(key,
                (value) => value,
                (why) => defaultValue);
        }

        private static Uri GetOptionalUri(string key)
        {
            return Settings.GetUri(key,
                (uri) => uri,
                (why) => default);
        }

        private static string GetOptionalCertificateBase64(string key)
        {
            return Settings.GetBase64Bytes(key,
                (bytes) => Convert.ToBase64String(bytes),
                (why) => default);
        }
    }
}
