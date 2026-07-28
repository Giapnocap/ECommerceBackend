namespace ECommerceBackend.Application.Exceptions
{
    public class NotFoundException : ApiException
    {
        public NotFoundException(string message)
            : base(404, "not_found", message) { }
    }
}
