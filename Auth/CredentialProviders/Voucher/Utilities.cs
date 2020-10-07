using System;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography;

namespace EastFive.Security.CredentialProvider.Voucher
{
    public static class Utilities
    {
        public static Uri GetTrustedProviderId()
        {
            var trustedVoucherProviderString = ConfigurationManager.AppSettings[AppSettings.CredentialProviderVoucherProviderId];
            var trustedVoucherProviderId = new Uri(trustedVoucherProviderString);
            return trustedVoucherProviderId;
        }

        public static string GenerateToken(Guid authId, DateTime validUntilUtc)
        {
            byte[] signatureData;
            var hashedData = ComputeHashData(authId, validUntilUtc, out signatureData);

            return AppSettings.CredentialProviderVoucherKey.RSAFromConfig(
                (trustedVoucherPrivateKey) =>
                {
                    var signature = trustedVoucherPrivateKey.SignHash(hashedData, CryptoConfig.MapNameToOID("SHA256"));
                    var tokenBytes = signatureData.Concat(signature).ToArray();
                    return Convert.ToBase64String(tokenBytes);
                },
                () => { throw new Exception(); },
                (issue) => { throw new Exception(); });
        }

        public static T ValidateToken<T>(string accessToken,
            Func<Guid, T> success,
            Func<string, T> tokenExpired,
            Func<string, T> invalidToken,
            Func<string, T> invalidSignature,
            Func<string, T> unspecifiedConfiguration)
        {
            #region Parse token

            long validUntilTicks = 0;
            var authId = default(Guid);
            var validUntilUtc = default(DateTime);
            var providedSignature = new byte[] { };
            try
            {
                var tokenBytes = Convert.FromBase64String(accessToken);

                var guidSize = Guid.NewGuid().ToByteArray().Length;
                const int dateTimeSize = sizeof(long);

                var authIdData = tokenBytes.Take(guidSize).ToArray();
                var validUntilUtcData = tokenBytes.Skip(guidSize).Take(dateTimeSize).ToArray();
                validUntilTicks = BitConverter.ToInt64(validUntilUtcData, 0);

                authId = new Guid(authIdData);
                validUntilUtc = new DateTime(validUntilTicks, DateTimeKind.Utc);
                providedSignature = tokenBytes.Skip(guidSize + dateTimeSize).ToArray();
            }
            catch (Exception ex)
            {
                invalidSignature("Cannot parse access token: " + ex.Message);
            }

            #endregion

            if (validUntilTicks < DateTime.UtcNow.Ticks)
                return tokenExpired("Token has expired");

            byte[] signatureData;
            var hashedData = ComputeHashData(authId, validUntilUtc, out signatureData);

            return AppSettings.CredentialProviderVoucherKey.RSAFromConfig(
                (trustedVoucher) =>
                {
                    if (!trustedVoucher.VerifyHash(hashedData, CryptoConfig.MapNameToOID("SHA256"), providedSignature))
                        return invalidSignature("Cannot verify hash - authId: " + authId +
                            "   validUntilUtc: " + validUntilUtc +
                            "   hashedData: " + hashedData +
                            "   providedSignature: " + providedSignature);

                    return success(authId);
                },
                () => unspecifiedConfiguration(AppSettings.CredentialProviderVoucherKey),
                (issue) => unspecifiedConfiguration($"{AppSettings.CredentialProviderVoucherKey}:{issue}"));
        }

        private static byte [] ComputeHashData(Guid authId, DateTime validUntilUtc, out byte [] signatureData)
        {
            var authIdData = authId.ToByteArray();
            var validUntilUtcData = BitConverter.GetBytes(validUntilUtc.Ticks);
            signatureData = authIdData.Concat(validUntilUtcData).ToArray();

            var hash = new SHA256Managed();

            var hashedData = hash.ComputeHash(signatureData);
            return hashedData;
        }
    }
}
