using System;
using System.Linq;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

using Microsoft.IdentityModel.Tokens;

using EastFive;
using EastFive.Extensions;
using EastFive.Collections.Generic;
using EastFive.Linq;
using EastFive.Net;
using System.Security.Claims;

namespace EastFive.Azure.Auth.OAuth
{
    public class Keys
    {
        public static Task<TResult> LoadTokenKeysAsync<TResult>(Uri jwksUri,
            Func<Keys, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            return jwksUri.HttpClientGetResourceAsync(
                onSuccess,
                onFailureToParse: (why, content) => onFailure("Key server returned a non-JSON response."),
                onFailure: onFailure);
        }

        public class Key
        {
            public string kty { get; set; }
            public string kid { get; set; }
            public string use { get; set; }
            public string alg { get; set; }
            public string n { get; set; }
            public string e { get; set; }

            public RSAParameters RSAParameters
            {
                get
                {
                    return new RSAParameters
                    {
                        Exponent = Base64UrlEncoder.DecodeBytes(this.e),
                        Modulus = Base64UrlEncoder.DecodeBytes(this.n),
                    };
                }
            }
        }

        public Key[] keys { get; set; }

        public TResult Parse<TResult>(string jwtEncodedString,
                string issuer, string[] validAudiences,
            Func<string, JwtSecurityToken, ClaimsPrincipal, TResult> onSuccess,
            Func<string, TResult> onInvalidToken)
        {
            // From: https://developer.apple.com/documentation/signinwithapplerestapi/verifying_a_user
            // To verify the identity token, your app server must:
            // * Verify the JWS E256 signature using the server’s public key
            // * Verify the nonce for the authentication
            // * Verify that the iss field contains https://appleid.apple.com
            // * Verify that the aud field is the developer’s client_id
            // * Verify that the time is earlier than the exp value of the token

            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwtEncodedString);

            return this.DecodeRSA(token.Header.Kid,
                rsaParams =>
                {
                    var validationParameters = new TokenValidationParameters()
                    {
                        ValidateAudience = true,
                        ValidIssuer = issuer,
                        ValidAudiences = validAudiences,
                        IssuerSigningKey = new RsaSecurityKey(rsaParams),
                        RequireExpirationTime = true,
                    };

                    try
                    {
                        var principal = handler.ValidateToken(jwtEncodedString, validationParameters,
                            out SecurityToken validatedToken);

                        return principal.Claims
                            .Where(claim => System.Security.Claims.ClaimTypes.NameIdentifier.Equals(claim.Type, StringComparison.OrdinalIgnoreCase))
                            .First(
                                (claim, next) =>
                                {
                                    return onSuccess(claim.Value, token, principal);
                                },
                                () => onInvalidToken("JWT did not specifiy a claims identity."));
                    }
                    catch (ArgumentException ex)
                    {
                        return onInvalidToken(ex.Message);
                    }
                    catch (global::Microsoft.IdentityModel.Tokens.SecurityTokenInvalidIssuerException ex)
                    {
                        return onInvalidToken(ex.Message);
                    }
                    catch (global::Microsoft.IdentityModel.Tokens.SecurityTokenExpiredException ex)
                    {
                        return onInvalidToken(ex.Message);
                    }
                    catch (global::Microsoft.IdentityModel.Tokens.SecurityTokenException ex)
                    {
                        return onInvalidToken(ex.Message);
                    }
                },
                () => onInvalidToken("Key does not match OAuth key tokens"));
        }

        private TResult DecodeRSA<TResult>(string keyId,
            Func<RSAParameters, TResult> onDecoded,
            Func<TResult> onNoMatch)
        {
            return this.keys
                .Where(key => key.kid == keyId)
                .First(
                    (key, next) =>
                    {
                        var parameters = key.RSAParameters;
                        return onDecoded(parameters);
                    },
                    onNoMatch);
        }
    }
}

