namespace ECommerceBackend.Application.Exceptions
{
    public class ConflictException : ApiException
    {
        public ConflictException(string message)
            : base(409, "conflict", message) { }

        public ConflictException(string message, Exception innerException)
            : this("conflict", message, innerException) { }

        public ConflictException(
            string code,
            string message,
            Exception? innerException = null)
            : base(409, code, message, innerException: innerException) { }
    }
}
