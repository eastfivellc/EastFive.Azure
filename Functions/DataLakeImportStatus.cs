using System;
namespace EastFive.Azure.Functions
{
    public enum DataLakeImportStatus
    {
        Running,
        Cancelled,
        Replaced,
        Complete,
        Partial,
        FaultedLocal,
        FaultedFile,
        FaultedInstance,
        FaultyExport,
    }
}

