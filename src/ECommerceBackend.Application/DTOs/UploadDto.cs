namespace ECommerceBackend.Application.DTOs
{
    public class UploadImageResponse
    {
        public Guid Id { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public bool IsMain { get; set; }
    }
}
