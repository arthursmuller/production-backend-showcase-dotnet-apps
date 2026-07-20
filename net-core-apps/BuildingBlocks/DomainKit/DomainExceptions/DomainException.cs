using System;

namespace DomainExceptions
{
    /// <summary>
    /// Base type for failures caused by a violated domain rule.
    /// The status code is transport-neutral metadata consumed by API boundaries.
    /// </summary>
    public abstract class DomainException : Exception
    {
        private const int DefaultStatusCode = 400;

        protected DomainException()
            : this("A domain rule was violated.")
        {
        }

        protected DomainException(string message, int statusCode = DefaultStatusCode)
            : base(message)
        {
            StatusCode = IsErrorStatusCode(statusCode)
                ? statusCode
                : DefaultStatusCode;
        }

        protected DomainException(
            string message,
            Exception innerException,
            int statusCode = DefaultStatusCode)
            : base(message, innerException)
        {
            StatusCode = IsErrorStatusCode(statusCode)
                ? statusCode
                : DefaultStatusCode;
        }

        public int StatusCode { get; }

        private static bool IsErrorStatusCode(int statusCode) =>
            statusCode is >= 400 and <= 599;
    }
}
