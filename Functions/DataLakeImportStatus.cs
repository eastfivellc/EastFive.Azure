using System;
namespace EastFive.Azure.Functions
{
    public enum DataLakeImportStatus
    {
        Running,
        Cancelled,
        Complete,
        Partial,
        FaultedLocal,
        FaultedFile,
        FaultedInstance,
        FaultyExport,
    }
}

