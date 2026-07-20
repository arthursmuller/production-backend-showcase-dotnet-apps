using System;

namespace AuthenticationProvider
{
    public class AuthenticationProviderException : Exception
    {
        public int StatusCode { get; set; }
        public AuthenticationProviderException(string message, int statusCode)
            : base(message) { StatusCode = statusCode; }
    }
}
