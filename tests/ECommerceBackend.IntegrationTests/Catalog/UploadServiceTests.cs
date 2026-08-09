using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public sealed class UploadServiceTests
{
    [Fact]
    public async Task UploadAndDeleteMainImage_PreserveSingleMainAndFilesystemState()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "ECommerceBackend.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await using var context = TestAppDbContext.Create();
            var category = new Category { Id = Guid.NewGuid(), Name = "Images" };
            var product = new Product
            {
                Id = Guid.NewGuid(),
                CategoryId = category.Id,
                Name = "Image Product",
                Description = "Image product",
                Price = 10m,
                StockQuantity = 1
            };
            context.AddRange(category, product);
            await context.SaveChangesAsync();
            var service = TestServiceFactory.CreateUploadService(
                context,
                new TestWebHostEnvironment(root));

            var first = await service.UploadProductImageAsync(
                product.Id,
                CreatePng("first.png"),
                isMain: false);
            var second = await service.UploadProductImageAsync(
                product.Id,
                CreatePng("second.png"),
                isMain: true);

            context.ChangeTracker.Clear();
            var images = await context.ProductImages
                .OrderBy(image => image.Id)
                .ToListAsync();
            Assert.Equal(2, images.Count);
            Assert.Single(images, image => image.IsMain && image.Id == second.Id);
            Assert.All(images, image => Assert.True(File.Exists(ToPhysicalPath(root, image.ImageUrl))));

            await service.DeleteProductImageAsync(product.Id, second.Id);

            context.ChangeTracker.Clear();
            var remaining = await context.ProductImages.SingleAsync();
            Assert.Equal(first.Id, remaining.Id);
            Assert.True(remaining.IsMain);
            Assert.False(File.Exists(ToPhysicalPath(root, second.ImageUrl)));
            Assert.True(File.Exists(ToPhysicalPath(root, remaining.ImageUrl)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    internal static IUploadFile CreatePng(string fileName)
    {
        byte[] content =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x00
        ];
        return new InMemoryUploadFile(
            fileName,
            "image/png",
            content);
    }

    private static string ToPhysicalPath(string root, string imageUrl)
    {
        const string requestPath = "/uploads/products/";
        if (!imageUrl.StartsWith(requestPath, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected product image URL '{imageUrl}'.");

        return Path.Combine(
            root,
            "Uploads",
            "products",
            imageUrl[requestPath.Length..]);
    }

    private sealed class InMemoryUploadFile : IUploadFile
    {
        private readonly byte[] _content;

        public InMemoryUploadFile(
            string fileName,
            string contentType,
            byte[] content)
        {
            FileName = fileName;
            ContentType = contentType;
            _content = content;
        }

        public string FileName { get; }
        public string ContentType { get; }
        public long Length => _content.LongLength;

        public Stream OpenReadStream()
            => new MemoryStream(_content, writable: false);
    }
}
