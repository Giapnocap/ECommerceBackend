namespace ECommerceBackend.Application.Exceptions
{
    public class ForbiddenException : ApiException
    {
        public ForbiddenException(string message = "Bạn không có quyền thực hiện thao tác này.")
            : base(403, "forbidden", message) { }
    }
}
