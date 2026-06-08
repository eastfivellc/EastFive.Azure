using System;
using System.Threading.Tasks;

namespace EastFive.Azure.Auth
{
    /// <summary>
    /// Optional attribute-interface that an application may declare (at
    /// <c>[assembly:]</c> scope) to resolve human-readable name details for an
    /// actor/account. Discovered via the domain attribute-interface scan by
    /// <see cref="ApplicationAuthExtensions.GetActorNameDetailsAsync{TResult}"/>.
    /// When no implementation is present the lookup resolves to "not found".
    /// </summary>
    public interface IProvideActorNameDetails
    {
        Task<TResult> GetActorNameDetailsAsync<TResult>(Guid actorId,
            Func<string, string, string, TResult> onActorFound,
            Func<TResult> onActorNotFound);
    }
}
