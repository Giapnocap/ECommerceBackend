using ECommerceBackend.API.Extensions;

namespace ECommerceBackend.Tests;

public sealed class ProductImageStaticFilesTests
{
    [Fact]
    public void StaticFiles_ExposeOnlySupportedImageContentTypes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ECommerceBackend.StaticFiles.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = ProductImageStaticFilesExtensions.CreateOptions(root);

            Assert.Equal(ProductImageStaticFilesExtensions.RequestPath, options.RequestPath.Value);
            Assert.True(options.ContentTypeProvider!.TryGetContentType("image.png", out var pngType));
            Assert.Equal("image/png", pngType);
            Assert.False(options.ContentTypeProvider.TryGetContentType("manual-note.txt", out _));
            Assert.False(options.ContentTypeProvider.TryGetContentType("image.svg", out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
