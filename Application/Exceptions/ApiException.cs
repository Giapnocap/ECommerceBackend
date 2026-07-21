namespace ECommerceBackend.Application.Exceptions
{
    public class ApiException : Exception
    {
        public int StatusCode { get; }
        public string Code { get; }
        public IReadOnlyDictionary<string, string[]>? Errors { get; }

        public ApiException(
            int statusCode,
            string code,
            string message,
            IReadOnlyDictionary<string, string[]>? errors = null,
            Exception? innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            Code = code;
            Errors = errors;
        }
    }
}
