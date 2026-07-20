using System;

namespace Feed.Domain.Exceptions
{
    public class FeedException : DomainExceptions.DomainException
    {
        public FeedException()
        {
        }

        public FeedException(string message)
            : base(message)
        {
        }

        public FeedException(string message, int statusCode)
            : base(message, statusCode)
        {
        }
    }
}
