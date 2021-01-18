using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Persistence.StorageTables
{
    /// <summary>
    /// An enumeration of the errors that can be seen from using the AST driver in C#.
    /// The full list of errors is available here: https://docs.microsoft.com/en-us/rest/api/storageservices/table-service-error-codes
    /// </summary>
    public enum ExtendedErrorInformationCodes
    {
        /// <summary>
        /// Bad Request (400): A property is specified more than one time.
        /// </summary>
        DuplicatePropertiesSpecified,

        /// <summary>
        /// Conflict (409): The specified entity already exists.
        /// </summary>
        EntityAlreadyExists,

        /// <summary>
        /// Bad Request (400): The entity is larger than the maximum size permitted.
        /// </summary>
        EntityTooLarge,

        /// <summary>
        /// Bad Request (400): The value specified is invalid.
        /// </summary>
        InvalidValueType,

        /// <summary>
        /// The '{0}' parameter of value '{1}' is out of range.
        /// </summary>
        OutOfRangeInput,

        /// <summary>
        ///  Bad Request (400): Values have not been specified for all properties in the entity.
        /// </summary>
        PropertiesNeedValue,

        /// <summary>
        /// Bad Request (400): The property name is invalid.
        /// </summary>
        PropertyNameInvalid,

        /// <summary>
        /// Bad Request (400): The property name exceeds the maximum allowed length.
        /// </summary>
        PropertyNameTooLong,

        /// <summary>
        /// Bad Request (400): The property value is larger than the maximum size permitted.
        /// </summary>
        PropertyValueTooLarge,

        /// <summary>
        /// Conflict (409): The table specified already exists.
        /// </summary>
        TableAlreadyExists,

        /// <summary>
        /// Conflict (409): The specified table is being deleted.
        /// </summary>
        TableBeingDeleted,

        /// <summary>
        /// Not Found (404): The table specified does not exist.
        /// </summary>
        TableNotFound,

        /// <summary>
        /// Bad Request (400): The entity contains more properties than allowed.
        /// </summary>
        TooManyProperties,

        /// <summary>
        /// Precondition Failed (412): The update condition specified in the request was not satisfied.
        /// </summary>
        UpdateConditionNotSatisfied,

        /// <summary>
        /// (408): There was a timeout while connecting to AST.
        /// </summary>
        Timeout,

        /// <summary>
        /// Error code that is not covered by this enum.
        /// </summary>
        Other,

        /// <summary>
        /// Error condition that was not specifed by extended information
        /// </summary>
        General,

        /// <summary>
        /// Bad Request (400): The value specified is invalid.
        /// </summary>
        InvalidValueGeneral,

        /// <summary>
        /// Status: 404 (The specified blob does not exist.)
        /// </summary>
        BlobNotFound,
    }
}
