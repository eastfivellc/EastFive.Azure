using System;
using System.Text.RegularExpressions;

using EastFive.Api;
using EastFive.Extensions;

namespace EastFive.Azure.Communications;

    /// <summary>
    /// Property attribute that validates and converts phone numbers to E.164 format.
    /// Accepts various phone number formats and normalizes them to +1XXXXXXXXXX for US numbers.
    /// </summary>
    public class PhoneNumberPropertyAttribute : PropertyAttribute
    {
        // Regex to extract digits from phone number
        private static readonly Regex DigitsOnly = new Regex(@"\D", RegexOptions.Compiled);

        /// <summary>
        /// Default country code to use when not specified (defaults to US/+1).
        /// </summary>
        public string DefaultCountryCode { get; set; } = "1";

        public override TResult Convert<TResult>(HttpApplication httpApp, Type type, object value,
            Func<object, TResult> onCasted,
            Func<string, TResult> onInvalid)
        {
            if (value == null || (value is string s && s.IsNullOrWhiteSpace()))
                return onInvalid("Phone number is required.");

            var phoneString = value.ToString();
            if (phoneString.IsNullOrWhiteSpace())
                return onInvalid("Phone number is required.");

            return TryConvertToE164(phoneString, DefaultCountryCode,
                e164 => onCasted(e164),
                onInvalid);
        }

        /// <summary>
        /// Attempts to convert a phone number string to E.164 format.
        /// </summary>
        public static TResult TryConvertToE164<TResult>(
            string phoneNumber,
            string defaultCountryCode,
            Func<string, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            if (phoneNumber.IsNullOrWhiteSpace())
                return onFailure("Phone number is required.");

            var trimmed = phoneNumber.Trim();

            // If already in E.164 format, validate and return
            if (trimmed.StartsWith("+"))
            {
                var digitsAfterPlus = DigitsOnly.Replace(trimmed.Substring(1), "");
                if (digitsAfterPlus.Length < 10 || digitsAfterPlus.Length > 15)
                    return onFailure($"Phone number '{phoneNumber}' has invalid length for E.164 format.");

                return onSuccess("+" + digitsAfterPlus);
            }

            // Extract only digits
            var digits = DigitsOnly.Replace(trimmed, "");

            if (digits.Length == 0)
                return onFailure($"Phone number '{phoneNumber}' contains no valid digits.");

            // Handle US phone numbers
            if (defaultCountryCode == "1")
            {
                // 10 digits: assume US number without country code
                if (digits.Length == 10)
                    return onSuccess("+1" + digits);

                // 11 digits starting with 1: US number with country code
                if (digits.Length == 11 && digits.StartsWith("1"))
                    return onSuccess("+" + digits);

                // 7 digits: local number without area code - reject
                if (digits.Length == 7)
                    return onFailure($"Phone number '{phoneNumber}' appears to be missing an area code.");

                return onFailure($"Phone number '{phoneNumber}' could not be converted to E.164 format. Expected 10 digits for US numbers.");
            }

            // For other countries, require at least 7 digits
            if (digits.Length < 7)
                return onFailure($"Phone number '{phoneNumber}' is too short.");

            if (digits.Length > 15)
                return onFailure($"Phone number '{phoneNumber}' is too long for E.164 format (max 15 digits).");

            // Prepend country code if not already present
            if (!digits.StartsWith(defaultCountryCode))
                return onSuccess("+" + defaultCountryCode + digits);

            return onSuccess("+" + digits);
        }
    }