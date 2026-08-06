namespace ECommerceBackend.Application.DTOs
{
    public class CategoryResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid? ParentId { get; set; }
        public string? ParentName { get; set; }
        public IEnumerable<CategoryResponse> Children { get; set; } = Enumerable.Empty<CategoryResponse>();
    }

    public class CreateCategoryRequest
    {
        public string Name { get; set; } = string.Empty;
        public Guid? ParentId { get; set; }
    }

    public class UpdateCategoryRequest
    {
        public string Name { get; set; } = string.Empty;
        public Guid? ParentId { get; set; }
    }
}
