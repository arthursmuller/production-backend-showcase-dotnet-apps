using System;

namespace FollowingProvider
{
    public class FollowingProviderException : Exception
    {
        public int StatusCode { get; set; } 
        public FollowingProviderException() { }

        public FollowingProviderException(string message)
            : base(message) { }

        public FollowingProviderException(string message, int statusCode)
            : base(message) { StatusCode = statusCode; }

        public FollowingProviderException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
