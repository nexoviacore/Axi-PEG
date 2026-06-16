using System;

namespace AxPeg.Exceptions
{
    public class PegException : Exception
    {
        public PegException(string message) : base(message) { }
        public PegException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class DatabaseException : PegException
    {
        public DatabaseException(string message) : base(message) { }
        public DatabaseException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class RedisException : PegException
    {
        public RedisException(string message) : base(message) { }
        public RedisException(string message, Exception innerException) : base(message, innerException) { }
    }
}
