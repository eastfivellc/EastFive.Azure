using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive;
using EastFive.Extensions;
using EastFive.Reflection;
using EastFive.Linq;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Collections.Generic;
using EastFive.Persistence;

namespace EastFive.Azure.Auth
{
    public delegate bool PopulatedByAuthorizationClaimPopulateValueDelegate(
        string claimType, out string claimValue);

    public interface PopulatedByAuthorizationClaim
    {
        TResource PopulateValue<TResource>(TResource accountToUpdate, MemberInfo member,
            PopulatedByAuthorizationClaimPopulateValueDelegate getClaimValue);
    }

    public class FromAuthorization : System.Attribute, PopulatedByAuthorizationClaim
    {
        public string ClaimType { get; set; }

        public TResource PopulateValue<TResource>(TResource accountToUpdate, MemberInfo member,
            PopulatedByAuthorizationClaimPopulateValueDelegate getClaimValue)
        {
            if (!getClaimValue(this.ClaimType, out string claimValue))
                return accountToUpdate;

            var memberType = member.GetPropertyOrFieldType();
            if (memberType.IsAssignableFrom(typeof(string)))
            {
                member.SetPropertyOrFieldValue(accountToUpdate, claimValue);
                return accountToUpdate;
            }
            if (memberType.IsAssignableFrom(typeof(Guid)))
            {
                if (Guid.TryParse(claimValue, out Guid guidValue))
                    member.SetPropertyOrFieldValue(accountToUpdate, guidValue);
                return accountToUpdate;
            }

            throw new NotImplementedException($"{nameof(FromAuthorization)} cannot cast claim to type {memberType.FullName}");
        }
    }

    
}

