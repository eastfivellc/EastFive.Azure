using System;
using System.Text.RegularExpressions;
using Azure.Communication.CallAutomation;
using Azure.Communication.PhoneNumbers;
using Azure.ResourceManager;
using EastFive.Azure.ResourceManagement;
using EastFive.Web.Configuration;


namespace EastFive.Azure.Communications;

public interface IProvideCommunicationsSettings
{
    string CommunicationsConnectionString { get; }
}

public static class CommunicationsSettingsExtensions
{
    public static TResult CreatePhoneNumbersClient<TResult>(
        this IProvideCommunicationsSettings settings,
        Func<PhoneNumbersClient, TResult> onClient,
        Func<string, TResult> onFailure)
    {
        return settings.CommunicationsConnectionString.ConfigurationString(
            connectionString =>
            {
                var client = new PhoneNumbersClient(connectionString);
                return onClient(client);
            },
            why => onFailure(why));
    }

    public static TResult CreateAutomationClient<TResult>(
        this IProvideCommunicationsSettings settings,
        Func<CallAutomationClient, TResult> onClient,
        Func<string, TResult> onFailure)
    {
        return settings.CommunicationsConnectionString.ConfigurationString(
            connectionString =>
            {
                var client = new CallAutomationClient(connectionString);
                return onClient(client);
            },
            why => onFailure(why));
    }

    /// <summary>
    /// Extracts the resource name from an ACS connection string.
    /// Example: endpoint=https://xyz.unitedstates.communication.azure.com/
    /// Returns: xyz
    /// </summary>
    public static TResult ExtractResourceName<TResult>(
        this IProvideCommunicationsSettings settings,
        Func<string, TResult> onExtracted,
        Func<string, TResult> onFailure)
    {
        return settings.CommunicationsConnectionString.ConfigurationString(
            connectionString =>
            {
                var endpointMatch = Regex.Match(connectionString, @"endpoint=https://([^.]+)\.", RegexOptions.IgnoreCase);
                if(endpointMatch.Success)
                    return onExtracted(endpointMatch.Groups[1].Value);
                return onFailure("Could not extract resource name from connection string.");
            },
            why => onFailure(why));
    }

    public static TResult CreateResourceManager<TResult>(
        this IProvideCommunicationsSettings settings, IProvideArmClientSettings armClientSettings,
        Func<ArmClient, string, TResult> onClient,
        Func<string, TResult> onFailure)
    {
        return settings.ExtractResourceName(
            resourceName =>
            {
                return armClientSettings.CreateARMClient(
                    client => onClient(client, resourceName),
                    why => onFailure(why));
            },
            why => onFailure(why));
    }

}