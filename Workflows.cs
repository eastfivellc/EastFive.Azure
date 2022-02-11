using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure
{
    public class Workflows
    {
        public class AuthorizationFlow
        {
            public const string FlowName = "Authorization Flow";
            public class Variables
            {
                public const string TokenName = "TOKEN";
                public const string AuthHeaderName = "AuthorizationHeaderName";
                public const string ApiVoucher = "ApiVoucher";
                public const string RedirectUrl = "RedirectUrl";
            }
        }

        public class HijackLoginFlow
        {
            public const string FlowName = "Hijack Login";
        }

        public class MonitoringFlow
        {
            public const string FlowName = "Monitoring";
            public class Variables
            {
                public const string CreatedNotification = "TeamsNotification";
                public const string FolderName = "FolderName";
            }
        }

        public class PasswordLoginCreateAccount
        {
            public const string FlowName = "Create Account";
            public class Variables
            {
                public const string UserId = "UserId";
                public const string Password = "Password";
                public const string State = "InternalAuthState";
                public const string Token = "InternalAuthToken";
                public const string AuthenticationId = "AuthenticationId";
                public const string Authorization = "AuthorizationId";
                public const string AuthorizationRedirect = "AuthorizationRedirect";
            }
        }
    }
}
