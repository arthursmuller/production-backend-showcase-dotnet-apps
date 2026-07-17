using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RichEnumeration
{
    public class RichEnumeratorException : Exception
    {
        public RichEnumeratorException()
        { }

        public RichEnumeratorException(string message)
            : base(message)
        { }

        public RichEnumeratorException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
