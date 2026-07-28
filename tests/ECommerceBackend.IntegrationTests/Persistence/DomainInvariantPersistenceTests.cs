using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ECommerceBackend.Tests;

public sealed class DomainInvariantPersistenceTests
{
    [Fact]
    public void CatalogRules_HaveDatabaseDefenses()
    {
        using var context = TestAppDbContext.Create();
        var model = context.GetService<IDesignTimeModel>().Model;
        var product = model.FindEntityType(typeof(Product))!;
        var category = model.FindEntityType(typeof(Category))!;

        AssertCheckConstraints(
            product,
            "CK_Products_Price_Positive",
            "CK_Products_Stock_NonNegative");
        AssertUniqueIndex(
            category,
            "UX_Categories_Root_NormalizedName",
            nameof(Category.NormalizedName));
        AssertUniqueIndex(
            category,
            "UX_Categories_ParentId_NormalizedName",
            nameof(Category.ParentId),
            nameof(Category.NormalizedName));
        AssertCheckConstraints(
            category,
            "CK_Categories_Parent_NotSelf");
    }

    [Fact]
    public void CartAndRoleRules_HaveDatabaseDefenses()
    {
        using var context = TestAppDbContext.Create();
        var model = context.GetService<IDesignTimeModel>().Model;
        var cart = model.FindEntityType(typeof(Cart))!;
        var cartItem = model.FindEntityType(typeof(CartItem))!;
        var userRole = model.FindEntityType(typeof(UserRole))!;
        var user = model.FindEntityType(typeof(User))!;

        AssertUniqueIndex(
            cart,
            null,
            nameof(Cart.UserId));
        AssertUniqueIndex(
            cartItem,
            "UX_CartItems_CartId_ProductId",
            nameof(CartItem.CartId),
            nameof(CartItem.ProductId));
        AssertCheckConstraints(
            cartItem,
            "CK_CartItems_Quantity_Positive",
            "CK_CartItems_UnitPrice_Positive");
        AssertUniqueIndex(
            userRole,
            null,
            nameof(UserRole.UserId));
        AssertCheckConstraints(
            user,
            "CK_Users_FailedLoginCount_NonNegative");
    }

    private static void AssertCheckConstraints(
        Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType entityType,
        params string[] expectedNames)
    {
        var names = entityType.GetCheckConstraints()
            .Select(constraint => constraint.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(expectedNames, name => Assert.Contains(name, names));
    }

    private static void AssertUniqueIndex(
        Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType entityType,
        string? databaseName,
        params string[] propertyNames)
    {
        var index = entityType.GetIndexes().Single(candidate =>
            candidate.IsUnique
            && candidate.Properties.Select(property => property.Name)
                .SequenceEqual(propertyNames, StringComparer.Ordinal)
            && (databaseName == null
                || candidate.GetDatabaseName() == databaseName));

        Assert.True(index.IsUnique);
    }
}
