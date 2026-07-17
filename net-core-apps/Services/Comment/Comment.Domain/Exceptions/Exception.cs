using System;

namespace Comment.Domain.Exceptions
{
    public class CommentException : Exception
    {
        public int StatusCode { get; set; }
        public CommentException() { }

        public CommentException(string message)
            : base(message) { }
        public CommentException(string message, int statusCode)
            : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
