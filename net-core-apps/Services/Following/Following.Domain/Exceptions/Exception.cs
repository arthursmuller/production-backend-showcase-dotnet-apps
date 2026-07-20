using System;

namespace Following.Domain.Exceptions
{
    public class FollowingException : DomainExceptions.DomainException
    {
        public FollowingException()
        {
        }

        public FollowingException(string message)
            : base(message)
        {
        }

        public FollowingException(string message, int statusCode)
            : base(message, statusCode)
        {
        }
    }
}
