namespace EastFive.Azure
{
    public class Workflows
    {
        public class AuthorizationFlow
        {
            public const string FlowName = "AuthorizationFlow";
            public const string Version = "2023.8.10";

            public static class Scopes
            {
                // WorkflowStep.Scope
                public const string Voucher = "Voucher";
            }

            public static class Steps
            {
                public const string ListVouchers = "List Vouchers";
                public const string ChooseVoucher = "Choose Voucher";
                public const string CreateVoucher = "Add Voucher";
                public const string UpdateVoucher = "Modify Voucher";
                public const string SecurityLog = "Security Log";
                public const string ActivityLog = "Activity Log";
            }

            public static class Ordinals
            {
                public const double ListVouchers = 1.0;
                public const double ChooseVoucher = 1.1;
                public const double CreateVoucher = 1.2;
                public const double UpdateVoucher = 1.3;
                public const double SecurityLog = 1.4;
                public const double ActivityLog = 1.5;
            }

            public static class Variables
            {
                public static class ShowExpired
                {
                    public static class Set
                    {
                        // WorkflowParameter.Value
                        public const string Value = "false";
                        // WorkflowParameter.Description
                        public const string Description = "Enter show expired [true | false]";
                    }
                }

                public static class VoucherId
                {
                    public static class Set
                    {
                        // WorkflowParameter.Value
                        public const string Value = "";
                        // WorkflowParameter.Description
                        public const string Description = "Enter voucher id";
                    }
                    public static class Get
                    {
                        // WorkflowParameter.Value or WorkflowVariable.VariableName
                        public const string Value = "VoucherId";
                        // WorkflowParameter.Description
                        public const string Description = "Run Choose Voucher first to select a value";
                    }
                }

                public static class AuthId
                {
                    public static class Set
                    {
                        // WorkflowParameter.Value
                        public const string Value = "";
                        // WorkflowParameter.Description
                        public const string Description = "Enter authorization id";
                    }
                }

                public static class VoucherDescription
                {
                    public static class Set
                    {
                        // WorkflowParameter.Value
                        public const string Value = "";
                        // WorkflowParameter.Description
                        public const string Description = "Enter description";
                    }
                }

                public static class VoucherExpiration
                {
                    public static class Set
                    {
                        // WorkflowParameter.Value
                        public const string Value = "";
                        // WorkflowParameter.Description
                        public const string Description = "Enter expiration date";
                    }
                }

                public static class MonitoringWhen
                {
                    public static class Set
                    {
                        // WorkflowParameter.Value
                        public const string Value = "";
                        // WorkflowParameter.Description
                        public const string Description = "Enter search when";
                    }
                }

                public static class MonitoringRoute
                {
                    public static class Set
                    {
                        // WorkflowParameter.Value
                        public const string Value = "";
                        // WorkflowParameter.Description
                        public const string Description = "Blank or enter route";
                    }
                }

                public static class MonitoringMethod
                {
                    public static class Set
                    {
                        // WorkflowParameter.Value
                        public const string Value = "";
                        // WorkflowParameter.Description
                        public const string Description = "Blank or enter HTTP method";
                    }
                }

                public const string TokenName = "TOKEN";
                public const string AuthHeaderName = "AuthorizationHeaderName";
                public const string ApiVoucher = "ApiVoucher";
                public const string RedirectUrl = "RedirectUrl";
            }
        }

        public class HijackLoginFlow
        {
            // WorkflowStep.FlowName
            public const string FlowName = "HijackLogin";
            public const string Version = "2024.1.19";

            public static class Steps
            {
                // WorkflowStep.StepName
                public const string ListMethods = "List Methods";
                public const string ChooseMethod = "Choose Method";
                public const string ListLogins = "List Logins";
                public const string ChooseLogin = "Choose Login";
                //public const string LaunchLogin = "Launch Login";
            }

            public static class Ordinals
            {
                // WorkflowStep.Step
                public const double ListMethods = 1.0;
                public const double ChooseMethod = 1.1;
                public const double ListLogins = 2.0;
                public const double ChooseLogin = 2.1;
                //public const double LaunchLogin = 3.0;
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
                        public const string Description = "Blank or search phrase (use ; to separate and \\; if there is a semicolon in the name)";
                    }
                }

                public static class ValidTokens
                {
                    public static class Set
                    {
                        // WorkflowParameter.Value
                        public const string Value = "true";
                        // WorkflowParameter.Description
                        public const string Description = "Show valid tokens only";
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
                    public static class Get
                    {
                        // WorkflowParameter.Value or WorkflowVariable.VariableName
                        public const string Value = "AuthorizationId";
                        // WorkflowParameter.Description
                        public const string Description = "Run Choose Login first to select a value";
                    }

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
            public const string Version = "2023.1.4";

            public class Variables
            {
                public const string CreatedNotification = "TeamsNotification";
                public const string FolderName = "FolderName";
            }
        }

        public class PasswordLoginCreateAccount
        {
            public const string FlowName = "CreateAccount";
            public const string Version = "2023.1.4";

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
