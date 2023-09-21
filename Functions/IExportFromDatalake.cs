using System;
namespace EastFive.Azure.Functions
{
	public interface IExportFromDatalake : IReferenceable
    {
        string exportContainer { get; }
        string exportFolder { get; }
    }
}

