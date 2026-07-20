using System;

namespace Comment.Domain.Exceptions
{
    public class CommentException : DomainExceptions.DomainException
    {
        public CommentException()
        {
        }

        public CommentException(string message)
            : base(message)
        {
        }

        public CommentException(string message, int statusCode)
            : base(message, statusCode)
        {
        }
    }
}
