#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Azure.Communication.PhoneNumbers;
using EastFive;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq.Async;

namespace EastFive.Azure.Communications
{
    public partial struct AcsPhoneNumber
    {
        #region AzureCommunicationService Integration

        /// <summary>
        /// Gets the AzureCommunicationService reference for phone numbers.
        /// Returns the first phone number's reference, or discovers the ACS resource if needed.
        /// </summary>
        public static async Task<TResult> GetAzureCommunicationServiceAsync<TResult>(
            Func<AzureCommunicationService, TResult> onFound,
            Func<TResult> onNotFound)
        {
            // First try to get from an existing phone number
            var phoneNumbers = await typeof(AcsPhoneNumber)
                .StorageGetAll()
                .CastObjsAs<AcsPhoneNumber>()
                .Where(p => p.azureCommunicationServiceRef != null)
                .ToArrayAsync();

            if (phoneNumbers.Any())
            {
                var acsRef = phoneNumbers.First().azureCommunicationServiceRef!;
                return await acsRef.StorageGetAsync(
                    acs => onFound(acs),
                    () => onNotFound());
            }

            // Fall back to AzureCommunicationService storage
            return await AzureCommunicationService.GetAsync(
                acs => onFound(acs),
                () => onNotFound());
        }

        /// <summary>
        /// Links all phone numbers to the specified AzureCommunicationService.
        /// Called after discovery to update existing phone number records.
        /// </summary>
        public static async Task LinkToAzureCommunicationServiceAsync(
            IRef<AzureCommunicationService> acsRef)
        {
            var phoneNumbers = await typeof(AcsPhoneNumber)
                .StorageGetAll()
                .CastObjsAs<AcsPhoneNumber>()
                .Where(p => p.azureCommunicationServiceRef == null)
                .ToArrayAsync();

            foreach (var phoneNumber in phoneNumbers)
            {
                await phoneNumber.acsPhoneNumberRef.StorageUpdateAsync(
                    async (current, saveAsync) =>
                    {
                        current.azureCommunicationServiceRef = acsRef;
                        await saveAsync(current);
                        return true;
                    },
                    () => false);
            }
        }

        #endregion



        #region Sync Methods

        /// <summary>
        /// Synchronizes phone numbers from Azure Communication Services.
        /// Performs a full sync: adds new numbers, updates existing, and removes deleted numbers.
        /// Also discovers and caches the ACS Resource ID for Event Grid subscription setup.
        /// </summary>
        private static async Task<TResult> SyncFromAzureAsync<TResult>(
            Func<TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            // Discover the Communication Services Resource ID (needed for Event Grid subscriptions)
            // var discoveryResult = await DiscoverCommunicationServiceResourceIdAsync();
            // if (!discoveryResult.success)
            // {
            //     // Log but don't fail - phone number sync can still proceed
            //     System.Diagnostics.Debug.WriteLine(
            //         $"Warning: Could not discover ACS Resource ID: {discoveryResult.error}");
            // }

            return await EastFive.Azure.AppSettings.Communications.Default
                .CreatePhoneNumbersClient(
                    async client =>
                    {
                        try
                        {
                            var azureNumbers = new List<PurchasedPhoneNumber>();

                            await foreach (var number in client.GetPurchasedPhoneNumbersAsync())
                            {
                                azureNumbers.Add(number);
                            }

                            // Get all existing numbers from storage
                            var existingNumbers = await typeof(AcsPhoneNumber)
                                .StorageGetAll()
                                .CastObjsAs<AcsPhoneNumber>()
                                .ToArrayAsync();

                            var existingByPhoneNumber = existingNumbers
                                .ToDictionary(n => n.phoneNumber, n => n);

                            var azurePhoneNumbers = azureNumbers
                                .Select(n => n.PhoneNumber)
                                .ToHashSet();

                            // Upsert numbers from Azure
                            foreach (var azureNumber in azureNumbers)
                            {
                                var capabilities = new List<PhoneNumberCapability>();
                                if (azureNumber.Capabilities.Calling == PhoneNumberCapabilityType.Inbound ||
                                    azureNumber.Capabilities.Calling == PhoneNumberCapabilityType.InboundOutbound)
                                    capabilities.Add(PhoneNumberCapability.InboundCalling);
                                if (azureNumber.Capabilities.Calling == PhoneNumberCapabilityType.Outbound ||
                                    azureNumber.Capabilities.Calling == PhoneNumberCapabilityType.InboundOutbound)
                                    capabilities.Add(PhoneNumberCapability.OutboundCalling);
                                if (azureNumber.Capabilities.Sms == PhoneNumberCapabilityType.Inbound ||
                                    azureNumber.Capabilities.Sms == PhoneNumberCapabilityType.InboundOutbound)
                                    capabilities.Add(PhoneNumberCapability.InboundSms);
                                if (azureNumber.Capabilities.Sms == PhoneNumberCapabilityType.Outbound ||
                                    azureNumber.Capabilities.Sms == PhoneNumberCapabilityType.InboundOutbound)
                                    capabilities.Add(PhoneNumberCapability.OutboundSms);

                                if (existingByPhoneNumber.TryGetValue(azureNumber.PhoneNumber, out var existing))
                                {
                                    // Update existing record
                                    await existing.acsPhoneNumberRef.StorageUpdateAsync(
                                        async (current, saveAsync) =>
                                        {
                                            current.displayName = azureNumber.PhoneNumber;
                                            current.acsResourceId = azureNumber.Id;
                                            // current.communicationServiceResourceId = discoveryResult.resourceId;
                                            current.capabilities = capabilities.ToArray();
                                            current.phoneNumberType = azureNumber.PhoneNumberType.ToString();
                                            current.countryCode = azureNumber.CountryCode;
                                            await saveAsync(current);
                                            return true;
                                        },
                                        onNotFound: () => false);
                                }
                                else
                                {
                                    // Create new record with deterministic ID based on phone number
                                    var newRef = GenerateDeterministicRef(azureNumber.PhoneNumber);
                                    var newNumber = new AcsPhoneNumber
                                    {
                                        acsPhoneNumberRef = newRef,
                                        phoneNumber = azureNumber.PhoneNumber,
                                        displayName = azureNumber.PhoneNumber,
                                        acsResourceId = azureNumber.Id,
                                        // communicationServiceResourceId = discoveryResult.resourceId,
                                        capabilities = capabilities.ToArray(),
                                        phoneNumberType = azureNumber.PhoneNumberType.ToString(),
                                        countryCode = azureNumber.CountryCode,
                                    };

                                    await newNumber.StorageCreateAsync(
                                        (discard) => true,
                                        () => true); // Already exists is fine
                                }
                            }

                            // Delete numbers that no longer exist in Azure
                            var numbersToDelete = existingNumbers
                                .Where(n => !azurePhoneNumbers.Contains(n.phoneNumber));

                            foreach (var numberToDelete in numbersToDelete)
                            {
                                await numberToDelete.acsPhoneNumberRef.StorageDeleteAsync(
                                    (discard) => true,
                                    () => true);
                            }

                            return onSuccess();
                        }
                        catch (Exception ex)
                        {
                            return onFailure($"Failed to sync from Azure: {ex.Message}");
                        }
                    },
                    (why) => onFailure($"ACS connection string not configured: {why}").AsTask());
        }

        /// <summary>
        /// Generates a deterministic reference ID based on the phone number.
        /// This ensures the same phone number always gets the same ID.
        /// </summary>
        private static IRef<AcsPhoneNumber> GenerateDeterministicRef(string phoneNumber)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(phoneNumber));
            var guid = new Guid(hash.Take(16).ToArray());
            return guid.AsRef<AcsPhoneNumber>();
        }

        #endregion
    }
}
