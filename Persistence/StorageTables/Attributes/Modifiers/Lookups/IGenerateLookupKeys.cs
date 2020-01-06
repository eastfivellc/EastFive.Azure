using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EastFive.Persistence.Azure.StorageTables
{
    public interface IGenerateLookupKeys
    {
        /// <summary>
        /// Compute the AST Ref for each Lookup table that will be created/updated.
        /// </summary>
        /// <typeparam name="TEntity"></typeparam>
        /// <param name="decoratedMember">
        /// The member for which a lookup is being calcuated.
        /// </param>
        /// <param name="lookupValues">
        /// All the members, and their associated value, 
        /// used to compute a lookup.
        /// </param>
        /// <returns>AST Ref for each Lookup table that will be created/updated.</returns>
        /// <remarks>
        /// In most cases only one lookup table will be created.
        /// The exception being where the member generating the lookups is a collection
        /// and the resource should be addressable by any single member of the collection.
        /// </remarks>
        IEnumerable<IRefAst> GetLookupKeys(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues);

        /// <summary>
        /// Identifies which members are needed to compute the lookup hash.
        /// </summary>
        /// <param name="decoratedMember">
        /// The member for which a lookup is being calcuated.
        /// </param>
        /// <returns>List of members that are needed to compute the lookup hash.</returns>
        IEnumerable<MemberInfo> ProvideLookupMembers(MemberInfo decoratedMember);
    }
}
