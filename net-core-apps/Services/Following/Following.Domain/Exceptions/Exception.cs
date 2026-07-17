using System;

namespace Following.Domain.Exceptions
{
    public class FollowingException : Exception
    {
        public int StatusCode { get; set; }
        public FollowingException() { }

        public FollowingException(string message)
            : base(message) { }
        public FollowingException(string message, int statusCode)
            : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
