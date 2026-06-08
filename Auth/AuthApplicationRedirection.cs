using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Api.Extensions;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Reflection;
using EastFive.Web;
using EastFive.Azure.Auth.CredentialProviders;

namespace EastFive.Azure.Auth
{
    /// <summary>
    /// Computes post-authentication redirect URIs for any <see cref="IApplication"/>
    /// without requiring an <c>IAzureApplication</c>. Ported from
    /// <c>AzureApplication.GetRedirectUriAsync</c>, with the
    /// <see cref="IResolveRedirection"/> chain discovered through the domain
    /// attribute-interface scan rather than the application instance's type.
    /// </summary>
    public static class AuthApplicationRedirection
    {
        public static async Task<TResult> GetRedirectUriAsync<TResult>(this IApplication application,
                Guid? accountIdMaybe, IDictionary<string, string> authParams,
                Method method, Authorization authorization,
                IHttpRequest request, IInvokeApplication endpoints,
                Uri baseUri, IProvideAuthorization authorizationProvider,
            Func<Uri, Func<IHttpResponse, IHttpResponse>, TResult> onSuccess,
            Func<string, string, TResult> onInvalidParameter,
            Func<TResult> onInvalidAccount,
            Func<string, TResult> onFailure)
        {
            async Task<TResult> finishUrlAsync(Uri redirect,
                KeyValuePair<string, string>[] kvps = default,
                Func<Authorization, Uri, Uri> authDecorator = default)
            {
                var (modifier, fullUri) = await application.ResolveAbsoluteUrlAsync(redirect,
                        request, accountIdMaybe, authParams);
                foreach (var kvp in kvps.NullToEmpty())
                    fullUri = fullUri.SetQueryParam(kvp.Key, kvp.Value);

                if (authDecorator == default)
                    return onSuccess(fullUri, x => x);

                var redirectDecorated = authDecorator(authorization, fullUri);
                return onSuccess(redirectDecorated, modifier);
            }

            if (!(authorizationProvider is IProvideRedirection))
                return await await ComputeRedirectAsync(accountIdMaybe, authParams,
                        method, authorization, endpoints,
                        authorizationProvider,
                    (fullUri) => finishUrlAsync(fullUri),
                    onInvalidParameter.AsAsyncFunc(),
                    onFailure.AsAsyncFunc());

            var redirectionProvider = authorizationProvider as IProvideRedirection;
            return await await redirectionProvider.GetRedirectUriAsync(accountIdMaybe,
                    authorizationProvider, authParams,
                    method, authorization,
                    application, request, endpoints, baseUri,
                (redirectUri, kvps) => finishUrlAsync(redirectUri, kvps, SetRedirectParameters),
                async () => await await ComputeRedirectAsync(accountIdMaybe, authParams,
                        method, authorization, endpoints,
                        authorizationProvider,
                    (fullUri) => finishUrlAsync(fullUri),
                    onInvalidParameter.AsAsyncFunc(),
                    onFailure.AsAsyncFunc()),
                onInvalidParameter.AsAsyncFunc(),
                onInvalidAccount.AsAsyncFunc(),
                onFailure.AsAsyncFunc());
        }

        public static Task<(Func<IHttpResponse, IHttpResponse>, Uri)> ResolveAbsoluteUrlAsync(this IApplication application,
            Uri relativeUri, IHttpRequest request, Guid? accountIdMaybe, IDictionary<string, string> authParams)
        {
            var fullUriStart = new Uri(request.RequestUri, relativeUri);
            Func<IHttpResponse, IHttpResponse> noModifications = m => m;
            return AttributeInterfaceScope
                .AttributeInterfacesInDomain<IResolveRedirection>()
                .Concat(application.AttributeInterfacesInApplication<IResolveRedirection>(inherit: true, multiple: true))
                .Distinct(attr => attr.GetType().FullName) // Issue with duplicate attributes due to Global.asax class
                .OrderBy(attr => attr.Order)
                .Aggregate((noModifications, fullUriStart).AsTask(),
                    async (relUriTask, redirResolver) =>
                    {
                        var (modifier, fullUri) = await relUriTask;
                        var (nextModifier, nextfullUri) = await redirResolver.ResolveAbsoluteUrlAsync(fullUri,
                            request, accountIdMaybe, authParams);
                        Func<IHttpResponse, IHttpResponse> combinedModifier = (response) =>
                        {
                            var nextResponse = nextModifier(response);
                            return modifier(nextResponse);
                        };
                        return (combinedModifier, nextfullUri);
                    });
        }

        private static async Task<TResult> ComputeRedirectAsync<TResult>(
                Guid? accountIdMaybe, IDictionary<string, string> authParams,
                Method method,
                Authorization authorization, IInvokeApplication endpoints,
                IProvideAuthorization authorizationProvider,
            Func<Uri, TResult> onSuccess,
            Func<string, string, TResult> onInvalidParameter,
            Func<string, TResult> onFailure)
        {
            if (!authorization.LocationAuthenticationReturn.IsDefaultOrNull())
            {
                if (authorization.LocationAuthenticationReturn.IsAbsoluteUri)
                {
                    var redirectUrl = SetRedirectParameters(authorization, authorization.LocationAuthenticationReturn);
                    return onSuccess(redirectUrl);
                }
                else
                {
                    var redirectUrl = EastFive.Web.Configuration.Settings.GetUri(
                        EastFive.Azure.AppSettings.Auth.LandingPage,
                        (landingPage) => new Uri(landingPage, authorization.LocationAuthenticationReturn),
                        (why) => default(Uri));
                    if (default != redirectUrl)
                    {
                        redirectUrl = SetRedirectParameters(authorization, redirectUrl);
                        return onSuccess(redirectUrl);
                    }
                }
            }

            if (null != authParams && authParams.ContainsKey(EastFive.Api.Azure.AzureApplication.ParameterRedirectUrl))
            {
                Uri redirectUri;
                var redirectUriString = authParams[EastFive.Api.Azure.AzureApplication.ParameterRedirectUrl];
                if (!Uri.TryCreate(redirectUriString, UriKind.Absolute, out redirectUri))
                    return onInvalidParameter("REDIRECT", $"BAD URL in redirect call:{redirectUriString}");
                var redirectUrl = SetRedirectParameters(authorization, redirectUri);
                return onSuccess(redirectUrl);
            }

            return await EastFive.Web.Configuration.Settings.GetUri(
                EastFive.Azure.AppSettings.Auth.LandingPage,
                (redirectUriLandingPage) =>
                {
                    var redirectUrl = SetRedirectParameters(authorization, redirectUriLandingPage);
                    return onSuccess(redirectUrl);
                },
                (why) => onFailure(why)).AsTask();
        }

        public static Uri SetRedirectParameters(Authorization authorization, Uri redirectUri)
        {
            var redirectUrl = redirectUri
                .SetQueryParam(EastFive.Api.Azure.AzureApplication.QueryRequestIdentfier,
                    authorization.authorizationRef.id.ToString());
            return redirectUrl;
        }
    }
}
