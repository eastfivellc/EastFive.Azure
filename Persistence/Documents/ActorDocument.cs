using System;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using Microsoft.Azure.Cosmos.Table;

using BlackBarLabs.Persistence.Azure.StorageTables;
using BlackBarLabs.Linq;
using EastFive.Linq;
using EastFive.Serialization;

namespace EastFive.Security.SessionServer.Persistence.Documents
{
    public class ActorMappingsDocument : TableEntity
    {
        #region Constructors

        public ActorMappingsDocument() { }

        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id { get { return Guid.Parse(this.RowKey); } }

        //internal async Task<Claim[]> GetClaims(AzureStorageRepository repository)
        //{
        //    var claimDocumentIds = Claims.ToGuidsFromByteArray();
        //    var claims = await claimDocumentIds
        //        .Select(
        //             async (claimDocumentId) =>
        //             {
        //                 return await repository.FindByIdAsync(claimDocumentId,
        //                     (ClaimDocument claimsDoc) =>
        //                     {
        //                         Uri issuer;
        //                         Uri.TryCreate(claimsDoc.Issuer, UriKind.RelativeOrAbsolute, out issuer);
        //                         Uri type;
        //                         Uri.TryCreate(claimsDoc.Type, UriKind.RelativeOrAbsolute, out type);
        //                         return new Claim
        //                         {
        //                             claimId = claimsDoc.ClaimId,
        //                             issuer = issuer,
        //                             type = type,
        //                             value = claimsDoc.Value,
        //                         };
        //                     },
        //                     () =>
        //                     {
        //                         // TODO: Flag data inconsitency
        //                         return default(Claim?);
        //                     });
        //             })
        //        .WhenAllAsync();
        //    return claims.Where(claim => claim.HasValue).Select(claim => claim.Value).ToArray();
        //}

        #endregion

        #region Properties

        public byte [] Claims { get; set; }


        internal async Task<TResult> AddOrUpdateClaimsAsync<TResult>(ClaimDocument claimsDoc, AzureStorageRepository repository,
            Func<TResult> success,
            Func<TResult> failure)
        {
            var claimDocumentIdsCurrent = Claims.ToGuidsFromByteArray();
            var updatedClaimDocumentIdsCurrent = claimDocumentIdsCurrent.AddIfNotExisting(claimsDoc.ClaimId);
            this.Claims = updatedClaimDocumentIdsCurrent.ToByteArrayOfGuids();
            var result = await repository.CreateOrUpdateAsync<ClaimDocument, TResult>(claimsDoc.ClaimId,
                async (created, doc, save) =>
                {
                    doc.ClaimId = claimsDoc.ClaimId;
                    doc.Issuer = claimsDoc.Issuer;
                    doc.Type = claimsDoc.Type;
                    doc.Value = claimsDoc.Value;
                    await save(doc);
                    return success();
                });
            return result;
        }

        public Guid[] GetClaims()
        {
            return this.Claims.ToGuidsFromByteArray();
        }

        public bool AddClaim(Guid claimId)
        {
            var claims = this.GetClaims();
            if (claims.Contains(claimId))
                return false;
            this.Claims = claims
                .Append(claimId)
                .ToByteArrayOfGuids();
            return true;
        }

        internal bool RemoveClaim(Guid claimId)
        {
            var claims = this.GetClaims();
            if (!claims.Contains(claimId))
                return false;
            this.Claims = claims
                .Where(rId => rId != claimId)
                .ToByteArrayOfGuids();
            return true;
        }
        
        public byte[] PasswordCredentials { get; set; }

        internal Guid[] GetPasswordCredentials()
        {
            return this.PasswordCredentials.ToGuidsFromByteArray();
        }

        internal bool AddPasswordCredential(Guid redirectId)
        {
            var redirectIds = this.GetPasswordCredentials();
            if (redirectIds.Contains(redirectId))
                return false;
            this.PasswordCredentials = redirectIds.Append(redirectId).ToByteArrayOfGuids();
            return true;
        }

        internal bool RemovePasswordCredential(Guid redirectId)
        {
            var redirectIds = this.GetPasswordCredentials();
            if (!redirectIds.Contains(redirectId))
                return false;
            this.PasswordCredentials = redirectIds.Where(rId => rId != redirectId).ToByteArrayOfGuids();
            return true;
        }

        public byte [] InviteIds { get; set; }

        internal bool AddInviteId(Guid inviteId)
        {
            var inviteIds = this.GetInviteIds();
            if (inviteIds.Contains(inviteId))
                return false;
            this.InviteIds = inviteIds.Append(inviteId).ToByteArrayOfGuids();
            return true;
        }

        internal bool RemoveInviteId(Guid inviteId)
        {
            var inviteIds = this.GetInviteIds();
            if (!inviteIds.Contains(inviteId))
                return false;
            this.InviteIds = inviteIds.Where(rId => rId != inviteId).ToByteArrayOfGuids();
            return true;
        }

        internal Guid[] GetInviteIds()
        {
            return this.InviteIds.ToGuidsFromByteArray();
        }


        #region Roles

        public byte[] Roles { get; set; }

        public Guid[] GetRoles()
        {
            return this.Roles.ToGuidsFromByteArray();
        }

        public bool AddRole(Guid roleId)
        {
            var roles = this.GetRoles();
            if (roles.Contains(roleId))
                return false;
            this.Roles = roles
                .Append(roleId)
                .ToByteArrayOfGuids();
            return true;
        }

        internal bool RemoveRole(Guid roleId)
        {
            var roles = this.GetRoles();
            if (!roles.Contains(roleId))
                return false;
            this.Roles = roles
                .Where(rId => rId != roleId)
                .ToByteArrayOfGuids();
            return true;
        }

        #endregion

        #endregion

    }
}
