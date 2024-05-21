using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
using System.Security;
using System.Runtime.InteropServices;
using System.Text.Unicode;

using Microsoft.Azure.Cosmos.Table;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Serialization;
using EastFive.Persistence;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Persistence.StorageTables
{
    public class StoreEncryptedAttribute : StorageAttribute
    {
        public string ConfigPropertySecret { get; set; }
        public string ConfigPropertySalt { get; set; }

        public StoreEncryptedAttribute(string configPropertySecret)
        {
            this.ConfigPropertySecret = configPropertySecret;
        }

        public override TResult GetMemberValue<TResult>(Type type,
                string propertyName, IDictionary<string, EntityProperty> values,
            Func<object, TResult> onBound, Func<TResult> onFailureToBind)
        {
            return BindEntityProperties(propertyName, type, values,
                onBound:onBound,
                onFailedToBind:() => base.GetMemberValue(type, propertyName, values, onBound, onFailureToBind));
        }

        protected override TResult BindEntityProperties<TResult>(string propertyName, Type type, 
            IDictionary<string, EntityProperty> allValues, 
            Func<object, TResult> onBound,
            Func<TResult> onFailedToBind)
        {
            return this.ConfigPropertySecret.ConfigurationString(
                (key) =>
                {
                    return this.ConfigPropertySalt.ConfigurationGuid(
                        (saltGuid) =>
                        {
                            var salt = saltGuid.ToByteArray();
                            if (!allValues.TryGetValue(propertyName, out var epValue))
                                return onFailedToBind();

                            if (epValue.PropertyType != EdmType.Binary)
                                return onFailedToBind();

                            byte[] decryptedValue = default;
                            try
                            {
                                decryptedValue = Decrypt2(key, epValue.BinaryValue, salt);
                            }
                            catch (System.Security.Cryptography.CryptographicException)
                            {
                                decryptedValue = new byte[] { };
                            }

                            if (type.IsAssignableFrom(typeof(SecureString)))
                            {
                                var sswString = Encoding.UTF8.GetString(decryptedValue);
                                var ssw = sswString.AsReadOnlySecureString();
                                return onBound(ssw);
                            }

                            if (type.IsAssignableFrom(typeof(string)))
                            {
                                var stringValue = Encoding.UTF8.GetString(decryptedValue);
                                return onBound(stringValue);
                            }

                            var objectValue = decryptedValue.FromByteArray(type);
                            return onBound(objectValue);
                        });
                });
        }

        public override KeyValuePair<string, EntityProperty>[] CastValue(Type typeOfValue, object value, string propertyName)
        {
            return this.ConfigPropertySecret.ConfigurationString(
                (key) =>
                {
                    return this.ConfigPropertySalt.ConfigurationGuid(
                        (saltGuid) =>
                        {
                            var salt = saltGuid.ToByteArray();

                            var cryptValue = GetEncryptedValue();
                            return propertyName
                                .PairWithValue(new EntityProperty(cryptValue))
                                .AsArray();

                            byte[] GetEncryptedValue()
                            {
                                var plainTextBytes = GetValueToSerialize();
                                return Encrypt2(key, plainTextBytes, salt);

                                byte[] GetValueToSerialize()
                                {
                                    if (typeof(SecureString).IsAssignableFrom(typeOfValue))
                                    {
                                        var ssw = (SecureString)value;
                                        return DecryptSecureString(ssw,
                                            ssValue =>
                                            {
                                                var ssBytes = Encoding.UTF8.GetBytes(ssValue);
                                                return ssBytes;
                                            });
                                    }

                                    if (typeof(string).IsAssignableFrom(typeOfValue))
                                    {
                                        var stringValue = (string)value;
                                        return Encoding.UTF8.GetBytes(stringValue);
                                    }

                                    var bytes = value.ToByteArray(typeOfValue);
                                    return bytes;
                                }
                            }
                        });
                });
        }

        /// <summary>
        /// Passes decrypted password String pinned in memory to Func delegate scrubbed on return.
        /// </summary>
        /// <typeparam name="T">Generic type returned by Func delegate</typeparam>
        /// <param name="action">Func delegate which will receive the decrypted password pinned in memory as a String object</param>
        /// <returns>Result of Func delegate</returns>
        public unsafe static T DecryptSecureString<T>(SecureString secureString, Func<string, T> action)
        {
            var insecureStringPointer = IntPtr.Zero;
            var insecureString = String.Empty;
            var gcHandler = GCHandle.Alloc(insecureString, GCHandleType.Pinned);

            try
            {
                insecureStringPointer = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                insecureString = Marshal.PtrToStringUni(insecureStringPointer);

                return action(insecureString);
            }
            finally
            {
                //clear memory immediately - don't wait for garbage collector
                fixed (char* ptr = insecureString)
                {
                    for (int i = 0; i < insecureString.Length; i++)
                    {
                        ptr[i] = '\0';
                    }
                }

                insecureString = null;

                gcHandler.Free();
                Marshal.ZeroFreeGlobalAllocUnicode(insecureStringPointer);
            }
        }

        public static byte[] Encrypt2(string password, byte[] plainText, byte[] salt)
        {
            int Rfc2898KeygenIterations = 100;
            int AesKeySizeInBits = 128;
            byte[] cipherText = null;
            using (Aes aes = new AesManaged())
            {
                aes.Padding = PaddingMode.PKCS7;
                aes.KeySize = AesKeySizeInBits;
                int KeyStrengthInBytes = aes.KeySize / 8;
                var rfc2898 = new Rfc2898DeriveBytes(password, salt, Rfc2898KeygenIterations);
                aes.Key = rfc2898.GetBytes(KeyStrengthInBytes);
                aes.IV = rfc2898.GetBytes(KeyStrengthInBytes);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(plainText, 0, plainText.Length);
                    }
                    cipherText = ms.ToArray();
                    return cipherText;
                }
            }
        }

        public static byte[] Decrypt2(string password, byte[] cipherText, byte[] salt)
        {
            int Rfc2898KeygenIterations = 100;
            int AesKeySizeInBits = 128;
            using (Aes aes = new AesManaged())
            {
                aes.Padding = PaddingMode.PKCS7;
                aes.KeySize = AesKeySizeInBits;
                int KeyStrengthInBytes = aes.KeySize / 8;
                var rfc2898 = new Rfc2898DeriveBytes(password, salt, Rfc2898KeygenIterations);
                aes.Key = rfc2898.GetBytes(KeyStrengthInBytes);
                aes.IV = rfc2898.GetBytes(KeyStrengthInBytes);

                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(cipherText, 0, cipherText.Length);
                    }
                    var plainText = ms.ToArray();
                    return plainText;
                }
            }
        }
    }
}
