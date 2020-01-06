using EastFive.Azure.Persistence.StorageTables;
using System;

namespace EastFive.Azure.StorageTables
{
    public class ExtendedErrorInformationException : Exception
    {
        public readonly ExtendedErrorInformationCodes code;

        public ExtendedErrorInformationException(ExtendedErrorInformationCodes code, string message)
            : base($"{code.ToString()}, {message}")
        {
            this.code = code;
        }
    }
}
