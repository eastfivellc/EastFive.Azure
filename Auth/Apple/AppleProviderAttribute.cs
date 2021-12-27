using System;
using System.Threading.Tasks;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Auth
{
    public class AppleProviderAttribute : Attribute, IProvideLoginProvider
    {
        private const string appleKeyServerUrl = "https://appleid.apple.com/auth/keys";

        public Task<TResult> ProvideLoginProviderAsync<TResult>(
            Func<IProvideLogin, TResult> onLoaded,
            Func<string, TResult> onNotAvailable)
        {
            return AppSettings.Auth.Apple.ClientId.ConfigurationString(
                applicationId =>
                {
                    return AppSettings.Auth.Apple.ValidAudiences.ConfigurationString(
                        (validAudiencesStr) =>
                        {
                            var validAudiences = validAudiencesStr.Split(','.AsArray());
                            return OAuth.Keys.LoadTokenKeysAsync(new Uri(appleKeyServerUrl),
                                keys =>
                                {
                                    var provider = new AppleProvider(applicationId, validAudiences, keys);
                                    return onLoaded(provider);
                                },
                                onFailure: onNotAvailable);
                        },
                        onNotAvailable.AsAsyncFunc());
                },
                onNotAvailable.AsAsyncFunc());
        }
    }
}

