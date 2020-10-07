using EastFive.Linq;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.AzureADB2C.Resources
{
    internal class KeyResource
    {
        public class Key
        {
            public string kid { get; set; }
            public int nbf { get; set; }
            public string use { get; set; }
            public string kty { get; set; }
            public string e { get; set; }
            public string n { get; set; }

            public SecurityKey GetKey()
            {
                var keyParams = new System.Security.Cryptography.RSAParameters()
                {
                    Exponent = KeyParamDecode(this.e),
                    Modulus = KeyParamDecode(this.n),
                };
                var key = new RsaSecurityKey(keyParams);
                key.KeyId = this.kid;
                return key;
            }
        }

        public Key [] keys { get; set; }

        public SecurityKey[] GetKeys()
        {
            return this.keys.NullToEmpty().Select(key => key.GetKey()).ToArray();
        }

        private static byte[] KeyParamDecode(string base64UrlText)
        {
            var qs = base64UrlText.Replace('-', '+').Replace('_', '/');
            int len = qs.Length % 4;
            if (len > 0) qs = qs.PadRight(qs.Length + (4 - len), '=');
            var bytes = System.Convert.FromBase64String(qs);
            return bytes;
        }
    }
}

//
//{
//  "keys": [
//    {
//      "kid": "IdTokenSigningKeyContainer.v2",
//      "nbf": 1459289287,
//      "use": "sig",
//      "kty": "RSA",
//      "e": "AQAB",
//      "n": "s4W7xjkQZP3OwG7PfRgcYKn8eRYXHiz1iK503fS-K2FZo-Ublwwa2xFZWpsUU_jtoVCwIkaqZuo6xoKtlMYXXvfVHGuKBHEBVn8b8x_57BQWz1d0KdrNXxuMvtFe6RzMqiMqzqZrzae4UqVCkYqcR9gQx66Ehq7hPmCxJCkg7ajo7fu6E7dPd34KH2HSYRsaaEA_BcKTeb9H1XE_qEKjog68wUU9Ekfl3FBIRN-1Ah_BoktGFoXyi_jt0-L0-gKcL1BLmUlGzMusvRbjI_0-qj-mc0utGdRjY-xIN2yBj8vl4DODO-wMwfp-cqZbCd9TENyHaTb8iA27s-73L3ExOQ"
//    },
//    {
//      "kid": "PftplvUMzdPZ8QgwW82Ss9Qbgxq5CgHbKYIH3dLkfsI",
//      "nbf": 1476384487,
//      "use": "sig",
//      "kty": "RSA",
//      "e": "AQAB",
//      "n": "rIq0vhs3xf12z9350Q2_MH3OC4eLGIKGWIm-GiM1K2WudzeWoLVQ6aSCbByA13yGbE_h8uAQSm2Z8vHS2hk67s4IU_EFOyjm-mqiGHvu2kKsmqL6pwxp28cPoqhlpaM0sQWwmJsRYEfEAb6Csej5YbdXqnz0LyxRt4vtrqt1BTRLCiWNx6slzglB0fy0j5dWQCD3Rpd9-nNbKcJgw5gn-VOVElqxN7HxYhMNSnnPLeJOEKjPSCmMXaXLcKdL--F1a3MBUspiZQ5O_mSFFJdSUKSZ5qamxfnbjf_mRK5enT9s2iepd0UAw-VVyuEEC4-U0VifINb2UaZ5ZtApl6qe4Q"
//    },
//    {
//      "kid": "gfIKIH-yZ3phRHRyjnsHIqZMaePLGAELzPat0CNY4sA",
//      "nbf": 1481069142,
//      "use": "sig",
//      "kty": "RSA",
//      "e": "AQAB",
//      "n": "538qDfJY4put42YpReCS32mEpMXabVKKP8alToo7MO0SgSvHABerybT1wK5fYXV_NkxIZrvQugwq0tCFdvAu9c1_ZM86u42mSOIvb05qH26SEusVwXNw_aFDFyccmmzjpK4_mv71fOqIVN3SAyUdVpDN6_gAPlh_ye_AjDKG7gUY_NaxwCKv_jTli_JuhOqS4jaop0cPq6StnhUMG9-Rn82i-bWY-4oQ8yWNY2In8J5sc2pkQDRYLBUgFsKKLVyA5u2XlftCn6OLDDrCk9yx4UK51_Dk1qLvzf4wArbi9GdkBkfVNx5OsrlDvVvgh8TOT1K22CLjXqC1YHzgpBOjdQ"
//    }
//  ]
//}