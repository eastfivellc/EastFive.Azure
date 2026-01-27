using System;
using System.Threading.Tasks;
using Azure.Identity;
using EastFive.Extensions;
using EastFive.Web.Configuration;
using Azure.ResourceManager;

namespace EastFive.Azure.ResourceManagement;

public interface IProvideArmClientSettings
{
    string TenantId { get; }
    string ClientId { get; }
    string ClientSecret { get; }
}

public static class ArmClientSettingsExtensions
{
    public static TResult CreateClientSecretCredential<TResult>(
        this IProvideArmClientSettings settings,
        Func<ClientSecretCredential, TResult> onClient,
        Func<string, TResult> onFailure)
    {
        return settings.TenantId.ConfigurationString(
            tenantId => settings.ClientId.ConfigurationString(
                clientId => settings.ClientSecret.ConfigurationString(
                    clientSecret =>
                    {
                        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                        return onClient(credential);
                    },
                    why => onFailure($"Client secret: {why}")),
                why => onFailure($"Client ID: {why}")),
            why => onFailure($"Tenant ID: {why}"));
    }

    public static TResult CreateARMClient<TResult>(
        this IProvideArmClientSettings settings,
        Func<ArmClient, TResult> onCreated,
        Func<string, TResult> onFailure)
    {
        return settings.CreateClientSecretCredential(
            clientSecretCred =>
            {
                var armClient = new ArmClient(clientSecretCred);
                return onCreated(armClient);
            },
            why => onFailure(why));
    }
}