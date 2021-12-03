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
            }
        }

        public class TeamsFlow
        {
            public const string FlowName = "Teams";

            public class Variables
            {
                public const string TokenName = "TOKEN";
                public const string AuthHeaderName = "AuthorizationHeaderName";
                public const string ApiVoucher = "ApiVoucher";
            }
        }
    }
}
