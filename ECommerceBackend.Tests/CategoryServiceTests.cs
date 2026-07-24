using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public class CategoryServiceTests
{
    [Fact]
    public async Task GetAllAsync_WhenRequestIsCancelled_StopsDatabaseQuery()
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateCategoryService(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetAllAsync(cancellation.Token));
    }

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

    [Fact]
    public async Task UpdateAsync_RenamesCategoryAndRejectsInvalidParentRelationships()
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateCategoryService(context);
        var parent = await service.CreateAsync(new CreateCategoryRequest { Name = "Parent" });
        var otherParent = await service.CreateAsync(new CreateCategoryRequest { Name = "Other Parent" });
        var child = await service.CreateAsync(new CreateCategoryRequest
        {
            Name = "Child",
            ParentId = parent.Id
        });

        var updated = await service.UpdateAsync(parent.Id, new UpdateCategoryRequest
        {
            Name = "Renamed Parent"
        });
        Assert.Equal("Renamed Parent", updated.Name);

        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(
            parent.Id,
            new UpdateCategoryRequest { Name = "Parent", ParentId = parent.Id }));
        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(
            parent.Id,
            new UpdateCategoryRequest { Name = "Parent", ParentId = child.Id }));
        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(
            parent.Id,
            new UpdateCategoryRequest { Name = "Parent", ParentId = otherParent.Id }));
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(
            new CreateCategoryRequest { Name = "Missing Parent", ParentId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(
            new CreateCategoryRequest { Name = "Grandchild", ParentId = child.Id }));
    }

    [Fact]
    public async Task DeleteAsync_DeletesEmptyCategoryAndProtectsNonEmptyCategory()
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateCategoryService(context);
        var empty = await service.CreateAsync(new CreateCategoryRequest { Name = "Empty" });
        var parent = await service.CreateAsync(new CreateCategoryRequest { Name = "Protected" });
        var withProduct = await service.CreateAsync(new CreateCategoryRequest { Name = "With Product" });
        _ = await service.CreateAsync(new CreateCategoryRequest
        {
            Name = "Protected Child",
            ParentId = parent.Id
        });
        context.Products.Add(new ECommerceBackend.Domain.Entities.Product
        {
            Id = Guid.NewGuid(),
            CategoryId = withProduct.Id,
            Name = "Protected Product",
            Price = 10,
            StockQuantity = 1,
            Description = "Category protection test",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        await service.DeleteAsync(empty.Id);

        Assert.True(await context.Categories
            .Where(category => category.Id == empty.Id)
            .Select(category => category.IsDeleted)
            .SingleAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(empty.Id));
        await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(parent.Id));
        await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(withProduct.Id));
    }
}
