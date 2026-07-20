using System;

namespace CategoriesProvider
{
    public class CategoriesProviderException : Exception
    {
        public int StatusCode { get; set; }
        public CategoriesProviderException(string message)
            : base(message)
        { }

        public CategoriesProviderException(string message, int statusCode)
            : base(message) { StatusCode = statusCode; }
    }
}
