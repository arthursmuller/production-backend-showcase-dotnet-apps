using System;

namespace Feed.Domain.Exceptions
{
    public class FeedException : Exception
    {
        public int StatusCode { get; set; }
        public FeedException() { }

        public FeedException(string message)
            : base(message) { }
        public FeedException(string message, int statusCode)
            : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
