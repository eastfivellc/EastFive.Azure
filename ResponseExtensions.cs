using System;

using Azure;

namespace EastFive.Azure;

public static class ResponseExtensions
{
    /// <summary>
    /// Safely extract the value from an Azure Response, handling null/error cases.
    /// Follows the TResult callback pattern used throughout EastFive.
    /// </summary>
    public static TResult TryGetValue<T, TResult>(this Response<T> response,
        Func<T, TResult> onValue,
        Func<TResult> onNoValue)
    {
        if (response is null)
            return onNoValue();

        if (response.Value is null)
            return onNoValue();

        return onValue(response.Value);
    }

    /// <summary>
    /// Safely extract the value from an Azure Response, with access to raw response.
    /// </summary>
    public static TResult TryGetValue<T, TResult>(this Response<T> response,
        Func<T, Response, TResult> onValue,
        Func<TResult> onNoValue)
    {
        if (response is null)
            return onNoValue();

        if (response.Value is null)
            return onNoValue();

        return onValue(response.Value, response.GetRawResponse());
    }

    /// <summary>
    /// Safely extract the value from an Azure Response, with error information.
    /// </summary>
    public static TResult TryGetValue<T, TResult>(this Response<T> response,
        Func<T, TResult> onValue,
        Func<int, string, TResult> onError)
    {
        if (response is null)
            return onError(0, "Response is null");

        var rawResponse = response.GetRawResponse();
        if (rawResponse.IsError)
            return onError(rawResponse.Status, rawResponse.ReasonPhrase);

        if (response.Value is null)
            return onError(rawResponse.Status, "Response value is null");

        return onValue(response.Value);
    }

    /// <summary>
    /// Check if the response has a valid value with out parameter.
    /// </summary>
    public static bool TryGetValue<T>(this Response<T> response, out T value)
    {
        if (response is null)
        {
            value = default!;
            return false;
        }

        if (response.Value is null)
        {
            value = default!;
            return false;
        }

        value = response.Value;
        return true;
    }
}
