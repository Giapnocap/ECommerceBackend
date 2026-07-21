using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Tests.Support;

namespace ECommerceBackend.Tests;

public class CategoryServiceTests
{
    [Fact]
    public async Task CreateAsync_NormalizesNameAndRejectsDuplicateInSameLevel()
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateCategoryService(context);

        var category = await service.CreateAsync(new CreateCategoryRequest { Name = "  Phones  " });
        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new CreateCategoryRequest { Name = "phones" }));

        Assert.Equal("Phones", category.Name);
        Assert.Equal("PHONES", context.Categories.Single(item => item.Id == category.Id).NormalizedName);
        Assert.Equal(409, exception.StatusCode);
    }
}
