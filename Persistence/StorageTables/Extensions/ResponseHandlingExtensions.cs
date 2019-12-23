using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Extensions;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace EastFive.Azure.StorageTables.Driver
{
    public static class ResponseHandlingExtensions
    {
        #region Case specific boolean checks

        public static bool IsProblemPreconditionFailed(this StorageException exception)
        {
            if (exception.InnerException is System.Net.WebException)
            {
                var webEx = (System.Net.WebException)exception.InnerException;
                if(webEx.Response is System.Net.HttpWebResponse)
                {
                    var httpResponse = (System.Net.HttpWebResponse)webEx.Response;
                    return (httpResponse.StatusCode == System.Net.HttpStatusCode.PreconditionFailed);
                }
            }
            return false;
        }

        public static bool IsProblemPropertyValueTooLarge(this StorageException exception)
        {
            return exception.ParseExtendedErrorInformation(
                (errorCode, errorMessage) => errorCode == ExtendedErrorInformationCodes.PropertyValueTooLarge,
                () => false);
        }
        
        public static bool IsProblemTimeout(this StorageException exception)
        {
            if (exception.InnerException is System.Net.WebException)
            {
                var webEx = (System.Net.WebException)exception.InnerException;
                return (webEx.Status == System.Net.WebExceptionStatus.Timeout);
            }
            if (408 == exception.RequestInformation.HttpStatusCode)
                return true;

            return false;
        }

        public static bool IsProblemResourceAlreadyExists(this StorageException exception)
        {
            return exception.ParseExtendedErrorInformation(
                (errorCode, errorMessage) => errorCode == ExtendedErrorInformationCodes.EntityAlreadyExists,
                () => false);
        }

        public static bool IsProblemTableDoesNotExist(this StorageException exception)
        {
            return exception.ParseExtendedErrorInformation(
                (errorCode, message) =>
                {
                    if (errorCode == ExtendedErrorInformationCodes.TableNotFound)
                        return true;
                    return ExtendedErrorCodeNotProvided();
                },
                () => ExtendedErrorCodeNotProvided());

            bool ExtendedErrorCodeNotProvided()
            {
                if (exception.InnerException is System.Net.WebException)
                {
                    var webEx = (System.Net.WebException)exception.InnerException;

                    if (webEx.Response is System.Net.HttpWebResponse)
                    {
                        var httpResponse = (System.Net.HttpWebResponse)webEx.Response;
                        return (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound);
                    }
                }
                return false;
            }
        }

        public static bool IsProblemDoesNotExist(this StorageException exception)
        {
            if (exception.InnerException is System.Net.WebException)
            {
                var webEx = (System.Net.WebException)exception.InnerException;

                if (webEx.Response is System.Net.HttpWebResponse)
                {
                    var httpResponse = (System.Net.HttpWebResponse)webEx.Response;
                    return (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound);
                }
            }
            return false;
        }

        #endregion

        public static Task<TResult> ResolveCreate<TResult>(this StorageException exception, 
            Microsoft.WindowsAzure.Storage.Table.CloudTable table,
            Func<TResult> retry,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            Func<TResult> onAlreadyExists = default,
            AzureStorageDriver.RetryDelegate onTimeout = default)
        {
            return exception.ParseStorageException(
                onEntityAlreadyExists: (msg) =>
                {
                    if (!onAlreadyExists.IsDefaultOrNull())
                        return onAlreadyExists().AsTask();
                    throw new ExtendedErrorInformationException(ExtendedErrorInformationCodes.EntityAlreadyExists, msg);
                },
                onNotFound: async (msg) => // IsProblemTableDoesNotExist
                {
                    try
                    {
                        await table.CreateIfNotExistsAsync();
                        return retry();
                    }
                    catch (StorageException createEx)
                    {
                        // Catch bug with azure storage table client library where
                        // if two resources attempt to create the table at the same
                        // time one gets a precondtion failed error.
                        System.Threading.Thread.Sleep(1000);
                        return retry();
                    }
                    catch (Exception) { throw; };
                },
                onTimeout: async (msg) => // IsProblemTimeout
                {
                    if (onTimeout.IsDefaultOrNull())
                        onTimeout = AzureStorageDriver.GetRetryDelegate();
                    bool shouldRetry = false;
                    await onTimeout(exception.RequestInformation.HttpStatusCode, exception,
                        () =>
                        {
                            shouldRetry = true;
                            return 1.AsTask();
                        });
                    if (shouldRetry)
                        return retry();
                    throw exception;
                },
                onDefaultCallback:
                (error, msg) =>
                {
                    if (!onFailure.IsDefaultOrNull())
                        return onFailure(error, msg).AsTask();
                    throw new ExtendedErrorInformationException(error, msg);
                });
        }

        public static TResult ParseStorageException<TResult>(this StorageException storageException,
            Func<string, TResult> onDuplicatePropertiesSpecified = default,
            Func<string, TResult> onEntityAlreadyExists = default,
            Func<string, TResult> onEntityTooLarge = default,
            Func<string, TResult> onInvalidValueType = default,
            Func<string, TResult> onOutOfRangeInput = default,
            Func<string, TResult> onPropertiesNeedValue = default,
            Func<string, TResult> onPropertyNameInvalid = default,
            Func<string, TResult> onPropertyNameTooLong = default,
            Func<string, TResult> onPropertyValueTooLarge = default,
            Func<string, TResult> onInvalidValueGeneral = default,
            Func<string, TResult> onTableAlreadyExists = default,
            Func<string, TResult> onTableBeingDeleted = default,
            Func<string, TResult> onNotFound = default,
            Func<string, TResult> onTooManyProperties = default,
            Func<string, TResult> onUpdateConditionNotSatisfied = default,
            Func<string, TResult> onTimeout = default,
            Func<string, TResult> onOther = default,
            Func<string, TResult> onGeneral = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onDefaultCallback = default)
        {
            return storageException.ParseStorageException(
                (errorCode, msg) =>
                {
                    switch(errorCode)
                    {
                        case ExtendedErrorInformationCodes.DuplicatePropertiesSpecified:
                            return ExecuteIfNotDefault(onDuplicatePropertiesSpecified);
                        case ExtendedErrorInformationCodes.EntityAlreadyExists:
                            return ExecuteIfNotDefault(onEntityAlreadyExists);
                        case ExtendedErrorInformationCodes.EntityTooLarge:
                            return ExecuteIfNotDefault(onEntityTooLarge);
                        case ExtendedErrorInformationCodes.InvalidValueType:
                            return ExecuteIfNotDefault(onInvalidValueType);
                        case ExtendedErrorInformationCodes.OutOfRangeInput:
                            return ExecuteIfNotDefault(onOutOfRangeInput);
                        case ExtendedErrorInformationCodes.PropertiesNeedValue:
                            return ExecuteIfNotDefault(onPropertiesNeedValue);
                        case ExtendedErrorInformationCodes.PropertyNameInvalid:
                            return ExecuteIfNotDefault(onPropertyNameInvalid);
                        case ExtendedErrorInformationCodes.PropertyNameTooLong:
                            return ExecuteIfNotDefault(onPropertyNameTooLong);
                        case ExtendedErrorInformationCodes.PropertyValueTooLarge:
                            return ExecuteIfNotDefault(onPropertyValueTooLarge);
                        case ExtendedErrorInformationCodes.TableAlreadyExists:
                            return ExecuteIfNotDefault(onTableAlreadyExists);
                        case ExtendedErrorInformationCodes.TableBeingDeleted:
                            return ExecuteIfNotDefault(onTableBeingDeleted);
                        case ExtendedErrorInformationCodes.TableNotFound:
                            return ExecuteIfNotDefault(onNotFound);
                        case ExtendedErrorInformationCodes.TooManyProperties:
                            return ExecuteIfNotDefault(onTooManyProperties);
                        case ExtendedErrorInformationCodes.UpdateConditionNotSatisfied:
                            return ExecuteIfNotDefault(onUpdateConditionNotSatisfied);
                        case ExtendedErrorInformationCodes.Timeout:
                            return ExecuteIfNotDefault(onTimeout);
                        case ExtendedErrorInformationCodes.Other:
                            return ExecuteIfNotDefault(onOther);
                        case ExtendedErrorInformationCodes.General:
                            return ExecuteIfNotDefault(onGeneral);
                        case ExtendedErrorInformationCodes.InvalidValueGeneral:
                            return ExecuteIfNotDefault(onInvalidValueGeneral);
                    }
                    throw ExMsg($"case for {errorCode}");

                    TResult ExecuteIfNotDefault(Func<string, TResult> func)
                    {
                        if (!func.IsDefaultOrNull())
                            return func(msg);
                        if (!onDefaultCallback.IsDefaultOrNull())
                            return onDefaultCallback(errorCode, msg);
                        throw storageException;
                    }
                },
                () =>
                {
                    if (storageException.InnerException.IsDefaultOrNull())
                        throw ExHandleMsg("exceptions", storageException);

                    if (storageException.InnerException is System.Net.WebException)
                    {
                        var webEx = (System.Net.WebException)storageException.InnerException;

                        if (webEx.Response is System.Net.HttpWebResponse)
                        {
                            var httpResponse = (System.Net.HttpWebResponse)webEx.Response;
                            var httpStatusCode = httpResponse.StatusCode;

                            if (httpStatusCode == HttpStatusCode.NotFound)
                                return onNotFound(webEx.Message);
                            if (httpStatusCode == HttpStatusCode.PreconditionFailed)
                                return onUpdateConditionNotSatisfied(webEx.Message);
                            if (httpStatusCode == HttpStatusCode.BadRequest)
                                return onInvalidValueGeneral(webEx.Message);
                            if (httpStatusCode == HttpStatusCode.RequestTimeout || httpStatusCode == HttpStatusCode.GatewayTimeout)
                                return onTimeout(webEx.Message);
                            throw ExHandleMsg("HttpResponse status code", httpStatusCode);
                        }
                        throw ExHandleMsg("inner exception response types", webEx.Response);
                    }
                    throw ExHandleMsg("inner exceptions", storageException.InnerException);

                    Exception ExHandleMsg(string handle, object type)
                    {
                        if(type.IsNull())
                            return new Exception($"{typeof(ResponseHandlingExtensions).FullName}.ParseStorageException `{handle}` -- Null Type");
                        if (type.GetType().IsEnum)
                            return ExMsg($"to handle {handle} of type {type}");
                        if (type.GetType() == typeof(string))
                            return ExMsg($"to handle {handle} of type {type}");
                        return ExHandleMsg(handle, type.GetType().FullName);
                    }
                });
            Exception ExMsg(string needMsg)
            {
                return new Exception($"{typeof(ResponseHandlingExtensions).FullName}.ParseStorageException needs {needMsg}");
            }
        }

        public static TResult ParseStorageException<TResult>(this StorageException storageException,
            Func<ExtendedErrorInformationCodes, string, TResult> onParsed,
            Func<TResult> onUnableToParse)
        {
            return storageException.ParseExtendedErrorInformation(
                onParsed,
                () =>
                {
                    if (storageException.IsProblemTimeout())
                        return onParsed(ExtendedErrorInformationCodes.Timeout, "The request to AST timed out.");

                    if (storageException.InnerException is System.Net.WebException)
                    {
                        var webEx = storageException.InnerException as System.Net.WebException;
                        if (webEx.Response is System.Net.HttpWebResponse)
                        {
                            var httpWebResponse = webEx.Response as System.Net.HttpWebResponse;
                            try
                            {
                                var responseContent = httpWebResponse.GetResponseStream().ReadAsString();
                                System.Diagnostics.Debug.WriteLine($"AST replied:{responseContent}");
                                return onParsed(ExtendedErrorInformationCodes.General, responseContent);
                            }
                            catch (Exception exFromStream)
                            {
                                exFromStream.GetType(); // Suppress warning
                                return onUnableToParse();
                            }
                        }
                    }
                    return onUnableToParse();
                });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="storageException"></param>
        /// <param name="onExtendedErrorInformation">Invoked with the RequestInformation.ExtendedErrorInformation. (ErrorCode and ErrorMessage, respectively) from the exception.</param>
        /// <param name="onExtendedErrorInformationNotProvided">Invoked when extended error information is not available</param>
        /// <returns></returns>
        public static TResult ParseExtendedErrorInformation<TResult>(this StorageException storageException,
            Func<ExtendedErrorInformationCodes, string, TResult> onExtendedErrorInformation,
            Func<TResult> onExtendedErrorInformationNotProvided)
        {
            if (storageException.RequestInformation.IsDefaultOrNull())
                return onExtendedErrorInformationNotProvided();
            if (storageException.RequestInformation.ExtendedErrorInformation.IsDefaultOrNull())
                return onExtendedErrorInformationNotProvided();

            var errorCode = storageException.RequestInformation.ExtendedErrorInformation.ErrorCode;
            var errorMessage = storageException.RequestInformation.ExtendedErrorInformation.ErrorMessage;
            System.Diagnostics.Debug.WriteLine($"AST Error [{errorCode}]:{errorMessage}");

            var matchingCodes = Enum.GetNames(typeof(ExtendedErrorInformationCodes))
                .Where(errorCodeName => String.Compare(errorCodeName, errorCode) == 0);
            
            if (!matchingCodes.Any())
                return onExtendedErrorInformation(ExtendedErrorInformationCodes.Other, errorMessage);

            var matchingCodeString = matchingCodes.First();
            Enum.TryParse(matchingCodeString, out ExtendedErrorInformationCodes matchingCode);
            return onExtendedErrorInformation(matchingCode, errorMessage);
        }
    }
}
