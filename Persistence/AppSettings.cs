using EastFive.Web;

namespace EastFive.Azure.Persistence
{
    [ConfigAttribute]
    public static class AppSettings
    {
        [ConfigKey("Default azure storage tables connection string",
            DeploymentOverrides.Suggested,
            DeploymentSecurityConcern = true,
            PrivateRepositoryOnly = true)]
        public const string Storage = "EastFive.Azure.StorageTables.ConnectionString";

        [ConfigKey("Default azure spa connection string",
            DeploymentOverrides.Suggested,
            DeploymentSecurityConcern = true,
            PrivateRepositoryOnly = true)]
        public const string SpaStorage = "EastFive.Azure.Spa.ConnectionString";

        [ConfigKey("Default azure spa container name",
            DeploymentOverrides.Suggested,
            DeploymentSecurityConcern = false,
            PrivateRepositoryOnly = true)]
        public const string SpaContainer = "EastFive.Azure.Spa.ContainerName";

        public static class Backup
        {
            [ConfigKey("The assemblies containing backup resources",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = false,
                PrivateRepositoryOnly = true)]
            public const string StorageResourceAssemblies = "EastFive.Azure.StorageTables.Backup.StorageResourceAssemblies";

            [ConfigKey("The seconds given to copy rows from a source table to a destination table",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = false,
                PrivateRepositoryOnly = true)]
            public const string SecondsGivenToCopyRows = "EastFive.Azure.StorageTables.Backup.SecondsGivenToCopyRows";

            [ConfigKey("The default backup destination connection string",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = true,
                PrivateRepositoryOnly = true)]
            public const string DefaultDestination = "EastFive.Azure.StorageTables.Backup.Default";

            [ConfigKey("The Sunday backup destination connection string",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = true,
                PrivateRepositoryOnly = true)]
            public const string SundayDestination = "EastFive.Azure.StorageTables.Backup.Sunday";

            [ConfigKey("The Monday backup destination connection string",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = true,
                PrivateRepositoryOnly = true)]
            public const string MondayDestination = "EastFive.Azure.StorageTables.Backup.Monday";

            [ConfigKey("The Tuesday backup destination connection string",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = true,
                PrivateRepositoryOnly = true)]
            public const string TuesdayDestination = "EastFive.Azure.StorageTables.Backup.Tuesday";

            [ConfigKey("The Wednesday backup destination connection string",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = true,
                PrivateRepositoryOnly = true)]
            public const string WednesdayDestination = "EastFive.Azure.StorageTables.Backup.Wednesday";

            [ConfigKey("The Thursday backup destination connection string",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = true,
                PrivateRepositoryOnly = true)]
            public const string ThursdayDestination = "EastFive.Azure.StorageTables.Backup.Thursday";

            [ConfigKey("The Friday backup destination connection string",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = true,
                PrivateRepositoryOnly = true)]
            public const string FridayDestination = "EastFive.Azure.StorageTables.Backup.Friday";

            [ConfigKey("The Saturday backup destination connection string",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = true,
                PrivateRepositoryOnly = true)]
            public const string SaturdayDestination = "EastFive.Azure.StorageTables.Backup.Saturday";

            [ConfigKey("The Month backup destination connection string",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = true,
                PrivateRepositoryOnly = true)]
            public const string MonthDestination = "EastFive.Azure.StorageTables.Backup.Month";

            [ConfigKey("The Quarter backup destination connection string",
                DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = true,
                PrivateRepositoryOnly = true)]
            public const string QuarterDestination = "EastFive.Azure.StorageTables.Backup.Quarter";

        }
    }
}
