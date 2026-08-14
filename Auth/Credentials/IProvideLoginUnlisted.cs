using System;

namespace EastFive.Azure.Auth
{
    /// <summary>
    /// Marker for <see cref="IProvideLogin"/> providers that must not appear in the
    /// public method listing (<c>GET /api/AuthenticationMethod</c>). The method still
    /// resolves by id/name server-side (session creation, admin flows) — it is only
    /// excluded from the roster login UIs build their provider pickers from. Used for
    /// methods whose authorizations are minted by trusted server-side code rather than
    /// an interactive login (e.g. account impersonation).
    /// </summary>
    public interface IProvideLoginUnlisted
    {
    }
}
