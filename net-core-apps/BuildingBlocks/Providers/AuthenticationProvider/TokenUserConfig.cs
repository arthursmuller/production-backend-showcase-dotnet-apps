using System;

namespace AuthenticationProvider
{
    public class AuthenticationProviderException : Exception
    {
        public int StatusCode { get; set; } 
        public AuthenticationProviderException() { }

        public AuthenticationProviderException(string message)
            : base(message) { }

        public AuthenticationProviderException(string message, int statusCode)
            : base(message) { StatusCode = statusCode; }

        public AuthenticationProviderException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
