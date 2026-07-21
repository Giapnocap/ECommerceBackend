namespace ECommerceBackend.Application.Exceptions
{
    public class BusinessException : ApiException
    {
        public BusinessException(string message)
            : this("business_error", message)
        {
        }

        public BusinessException(
            string code,
            string message,
            Exception? innerException = null)
            : base(400, code, message, innerException: innerException)
        {
        }
    }
}