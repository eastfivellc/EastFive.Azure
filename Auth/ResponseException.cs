using System;

namespace EastFive.Security.SessionServer.Exceptions
{
    public class ResponseException : Exception
    {
        public ResponseException()
            : base()
        {
        }

        public ResponseException(string message)
            : base(message)
        {
        }

        public ResponseException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}