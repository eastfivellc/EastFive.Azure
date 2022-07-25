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
            // WorkflowStep.FlowName
            public const string FlowName = "Hijack Login";

            public static class Steps
            {
                // WorkflowStep.StepName
                public const string ListMethods = "List Methods";
                public const string ChooseMethod = "Choose Method";
                public const string ListLogins = "List Logins";
                public const string ChooseLogin = "Choose Login";
                public const string LaunchLogin = "Launch Login";
            }

            public static class Ordinals
            {
                // WorkflowStep.Step
                public const double ListMethods = 1.0;
                public const double ChooseMethod = 1.1;
                public const double ListLogins = 2.0;
                public const double ChooseLogin = 2.1;
                public const double LaunchLogin = 3.0;
            }

            public static class Variables
            {
                public static class Method
                {
                    public static class Get
                    {
                        // WorkflowParameter.Value
                        public const string Value = "AuthenticationMethodId";
                        // WorkflowParameter.Description
                        public const string Description = "Run Choose Method first to select a value";
                    }

                    public static class Set
                    {
                        // WorkflowParameter.Value
                        public const string Value = "X";
                        // WorkflowParameter.Description
                        public const string Description = "Enter method (can run List Methods to find)";
                    }
                }

                public static class Search
                {
                    public static class Set
                    {
                        // WorkflowParameter.Value
                        public const string Value = "";
                        // WorkflowParameter.Description
                        public const string Description = "Blank or search phrase";
                    }
                }

                public static class History
                {
                    public static class Set
                    {
                        // WorkflowParameter.Value
                        public const string Value = "7";
                        // WorkflowParameter.Description
                        public const string Description = "Enter number of past days to search";
                    }
                }

                public static class Authorization
                {
                    public static class Set
                    {
                        // WorkflowParameter.Value
                        public const string Value = "X";
                        // WorkflowParameter.Description
                        public const string Description = "Enter authorization (can run List Logins to find)";
                    }
                }
            }
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
                public const string AccountId = "AccountId";
            }
        }
    }
}
