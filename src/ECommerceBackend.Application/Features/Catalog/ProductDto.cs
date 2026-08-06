namespace ECommerceBackend.Application.DTOs
{
    public class ProductImageResponse
    {
        public Guid Id { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public bool IsMain { get; set; }
    }

    public class ProductResponse
    {
        public Guid Id { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public string Description { get; set; } = string.Empty;
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public IEnumerable<ProductImageResponse> Images { get; set; } = Enumerable.Empty<ProductImageResponse>();
        public string? MainImageUrl => Images.FirstOrDefault(i => i.IsMain)?.ImageUrl
                                    ?? Images.FirstOrDefault()?.ImageUrl;
    }

    public sealed class ProductSummaryResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? MainImageUrl { get; set; }
    }

    public class CreateProductRequest
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public string Description { get; set; } = string.Empty;
        public Guid CategoryId { get; set; }
    }

    public class UpdateProductRequest
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public string Description { get; set; } = string.Empty;
        public Guid CategoryId { get; set; }
    }

    public class ProductQueryParams
    {
        public string? Keyword { get; set; }
        public Guid? CategoryId { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public string? SortBy { get; set; }       // name | price | createdAt
        public string? SortOrder { get; set; }    // asc | desc
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 12;
    }
}
