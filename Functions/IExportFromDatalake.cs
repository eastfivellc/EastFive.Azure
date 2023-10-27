using System;
namespace EastFive.Azure.Functions
{
	public interface IExportFromDatalake : IReferenceable
    {
        string exportContainer { get; }
        string exportFolder { get; }

        /// <summary>
        /// This is a hash that represents the current instance of the source data
        /// </summary>
        Guid sourceId { get; }
    }
}

