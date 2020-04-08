using System;
using System.Threading.Tasks;

namespace EastFive.Azure.Auth.CredentialProviders
{
    public struct LoginInfo
    {
        public Guid loginId;
        public string userName;
        public bool isEmail;
        public string otherMail;
        public bool forceChange;
        public bool accountEnabled;
        internal string displayName;
        internal bool forceChangePassword;

        public TResult GetEmail<TResult>(
            Func<string,TResult> onSuccess,
            Func<TResult> onNotFound)
        {
            if (isEmail)
                return onSuccess(userName);
            else if (!string.IsNullOrWhiteSpace(otherMail))
                return onSuccess(otherMail);
            else
                return onNotFound();
        }
    }

    public interface IProvideLoginManagement
    {
        string Method { get; }

        Task<TResult> CreateAuthorizationAsync<TResult>(string displayName,
            string userId, bool isEmail, string secret, bool forceChange,
            Func<Guid, TResult> onSuccess,
            Func<Guid, TResult> usernameAlreadyInUse,
            Func<TResult> onPasswordInsufficent,
            Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported,
            Func<string, TResult> onFailure);

        Task<TResult> GetAuthorizationAsync<TResult>(Guid loginId,
            Func<LoginInfo, TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported,
            Func<string, TResult> onFailure);

        Task<TResult> GetAllAuthorizationsAsync<TResult>(
            Func<LoginInfo[], TResult> onFound,
            Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported,
            Func<string, TResult> onFailure);

        Task<TResult> UpdateAuthorizationAsync<TResult>(Guid loginId, string password, bool forceChange,
            Func<TResult> onSuccess,
            Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported,
            Func<string, TResult> onFailure);

        Task<TResult> UpdateEmailAsync<TResult>(Guid loginId, string email,
            Func<TResult> onSuccess,
            Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported,
            Func<string, TResult> onFailure);

        Task<TResult> DeleteAuthorizationAsync<TResult>(Guid loginId,
            Func<TResult> onSuccess,
            Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported,
            Func<string, TResult> onFailure);
        
    }
}
