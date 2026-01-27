using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Azure.Communication.PhoneNumbers;

using EastFive;
using EastFive.Api;
using EastFive.Azure.Auth;
using EastFive.Azure.Communications;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Web.Configuration;

using Newtonsoft.Json;

namespace EastFive.Azure.Communications
{
    [FunctionViewController(
        Route = "AcsPhoneNumber",
        ContentType = "x-application/acs-phone-number")]
    public partial struct AcsPhoneNumber
    {
        #region GET

        /// <summary>
        /// Get a specific ACS phone number by ID.
        /// </summary>
        [HttpGet]
        [SuperAdminClaim]
        public static Task<IHttpResponse> GetByIdAsync(
                [QueryParameter(CheckFileName = true, Name = IdPropertyName)]
                IRef<AcsPhoneNumber> acsPhoneNumberRef,
            ContentTypeResponse<AcsPhoneNumber> onFound,
            NotFoundResponse onNotFound)
        {
            return acsPhoneNumberRef.StorageGetAsync(
                (phoneNumber) => onFound(phoneNumber),
                () => onNotFound());
        }

        /// <summary>
        /// Get all ACS phone numbers. When refresh=true, performs a full sync
        /// with Azure Communication Services, adding new numbers and removing
        /// numbers that no longer exist in Azure.
        /// </summary>
        [HttpGet]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> GetAllAsync(
                [QueryParameter(Name = "refresh")] bool refresh,
            MultipartAsyncResponse<AcsPhoneNumber> onSuccess,
            GeneralFailureResponse onFailure)
        {
            if (refresh)
            {
                var syncResult = await SyncFromAzureAsync(
                    onSuccess: () => (true, string.Empty),
                    onFailure: (reason) => (false, reason));

                if (!syncResult.Item1)
                    return onFailure(syncResult.Item2);
            }

            var phoneNumbers = typeof(AcsPhoneNumber)
                .StorageGetAll()
                .CastObjsAs<AcsPhoneNumber>();

            return onSuccess(phoneNumbers);
        }

        #endregion
    }
}
