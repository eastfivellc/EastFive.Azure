using System;
using System.Security.Cryptography;

using Newtonsoft.Json;

using EastFive.Api;
using EastFive.Security;

namespace EastFive.Azure.OAuth.Server
{
    /// <summary>
    /// GET /.well-known/jwks.json — the RSA public key used to sign access/session
    /// tokens (EastFive.Security.Token.Key) as a JSON Web Key Set (RFC 7517).
    /// </summary>
    [FunctionViewController(
        Namespace = ".well-known",
        Route = "jwks.json",
        ContentType = "application/json")]
    public class JsonWebKeySet
    {
        [Unsecured("JWKS documents are public by definition — they expose only the public signing key.")]
        [HttpGet(MatchAllParameters = false)]
        public static IHttpResponse Get(
            ContentTypeResponse<JwksDocument> onFound,
            ConfigurationFailureResponse onConfigurationFailure)
        {
            return EastFive.Security.AppSettings.TokenKey.RSAFromConfig(
                rsaProvider =>
                {
                    using (rsaProvider)
                    {
                        var parameters = rsaProvider.ExportParameters(includePrivateParameters: false);
                        var key = JwkKey.FromRsaParameters(parameters);
                        var document = new JwksDocument { Keys = new[] { key } };
                        return onFound(document, "application/json");
                    }
                },
                () => onConfigurationFailure(
                    EastFive.Security.AppSettings.TokenKey, "Token signing key is not configured."),
                (why) => onConfigurationFailure(
                    EastFive.Security.AppSettings.TokenKey, why));
        }
    }

    public class JwksDocument
    {
        [JsonProperty("keys")]
        public JwkKey[] Keys { get; set; }
    }

    public class JwkKey
    {
        [JsonProperty("kty")]
        public string KeyType { get; set; }

        [JsonProperty("use")]
        public string Use { get; set; }

        [JsonProperty("alg")]
        public string Algorithm { get; set; }

        [JsonProperty("kid")]
        public string KeyId { get; set; }

        [JsonProperty("n")]
        public string Modulus { get; set; }

        [JsonProperty("e")]
        public string Exponent { get; set; }

        public static JwkKey FromRsaParameters(RSAParameters parameters)
        {
            var modulus = OAuthServer.Base64UrlEncode(parameters.Modulus);
            var exponent = OAuthServer.Base64UrlEncode(parameters.Exponent);
            var kid = ComputeKeyId(parameters);
            return new JwkKey
            {
                KeyType = "RSA",
                Use = "sig",
                Algorithm = "RS256",
                KeyId = kid,
                Modulus = modulus,
                Exponent = exponent,
            };
        }

        /// <summary>Stable key id derived from the public key material.</summary>
        public static string ComputeKeyId(RSAParameters parameters)
        {
            var material = new byte[parameters.Modulus.Length + parameters.Exponent.Length];
            parameters.Modulus.CopyTo(material, 0);
            parameters.Exponent.CopyTo(material, parameters.Modulus.Length);
            var hash = SHA256.HashData(material);
            return OAuthServer.Base64UrlEncode(hash[..16]);
        }
    }
}
