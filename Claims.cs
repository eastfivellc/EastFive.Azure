using System;
using System.Threading.Tasks;
using System.Linq;

namespace EastFive.Security.SessionServer
{
    public class Claims
    {
        private Context context;
        private Persistence.DataContext dataContext;

        internal Claims(Context context, Persistence.DataContext dataContext)
        {
            this.dataContext = dataContext;
            this.context = context;
        }

        public async Task<TResult> CreateOrUpdateAsync<TResult>(Guid accountId, Guid claimId, string type, string value,
            Func<TResult> onSuccess,
            Func<TResult> onFailure,
            Func<TResult> onActorNotFound)
        {
            return await dataContext.Claims.CreateOrUpdateAsync(accountId, claimId, type, value,
                onSuccess,
                onFailure,
                onActorNotFound);
        }

        public async Task<TResult> FindByAccountIdAsync<TResult>(Guid accountId,
            Func<System.Security.Claims.Claim[], TResult> found,
            Func<TResult> accountNotFound)
        {
            var result = await dataContext.Claims.FindAsync<TResult>(accountId,
                claims =>
                {
                    var claimsSystem = claims.Select(
                            claim =>
                            {
                                var claimName = claim.type.ToString();
                                var claimValue = claim.value;
                                var claimSystem = new System.Security.Claims.Claim(claimName, claimValue);
                                return claimSystem;
                            }).ToArray();
                    try
                    {
                        var claimResult = found(claimsSystem);
                        return claimResult;
                    } catch(Exception ex)
                    {
                        ex.GetType();
                        return found(claimsSystem);
                    }
                },
                accountNotFound);
            return result;
        }



            //throw new NotImplementedException();
            ////return await this.dataContext.Authorizations.UpdateClaims(authorizationId,
            ////    (claimsStored, addClaim) =>
            ////    {
            ////        var claims = EnumerableAsync.YieldAsync<Func<Guid, Guid, Uri, string, Task>>(
            ////            async (yieldAsync) =>
            ////            {
            ////                foreach (var claim in claimsStored)
            ////                {
            ////                    if (default(Uri) == type ||
            ////                        String.Compare(type.AbsoluteUri, claim.type.AbsoluteUri) == 0)
            ////                    {
            ////                        await yieldAsync(claim.claimId, authorizationId, claim.type, claim.value);
            ////                    }
            ////                }
            ////            });
            ////        return Task.FromResult(found(claims));
            ////    },
            ////    // TODO: Create and use dataContext.Authorizations.FindClaims since next two methods are mute since addClaim is never invoked
            ////    () => true,
            ////    () => false,
            ////    () => authorizationNotFound(),
            ////    (whyFailed) => failure(whyFailed));

    }
}
