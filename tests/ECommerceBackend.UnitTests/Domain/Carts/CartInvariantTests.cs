using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Tests;

public sealed class CartInvariantTests
{
    [Fact]
    public void AddAndIncreaseItem_EnforcesStockAndRefreshesUnitPrice()
    {
        var cart = Cart.Create(Guid.NewGuid(), Guid.NewGuid());
        var product = CreateProduct(price: 10m, stock: 5);
        var item = cart.AddItem(Guid.NewGuid(), product, 2);
        product.UpdateDetails(
            product.CategoryId,
            product.Name,
            12m,
            product.Description);

        item.IncreaseQuantity(2, product);

        Assert.Equal(4, item.Quantity);
        Assert.Equal(12m, item.UnitPrice);

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            item.IncreaseQuantity(2, product));
        Assert.Equal("business_error", exception.Code);
        Assert.Equal(4, item.Quantity);
        Assert.Equal(12m, item.UnitPrice);
    }

    [Fact]
    public void SetQuantity_RejectsUnavailableProductWithoutMutation()
    {
        var cart = Cart.Create(Guid.NewGuid(), Guid.NewGuid());
        var product = CreateProduct(price: 10m, stock: 5);
        var item = cart.AddItem(Guid.NewGuid(), product, 2);
        product.MarkDeleted();

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            item.SetQuantity(3, product));

        Assert.Equal("business_error", exception.Code);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(10m, item.UnitPrice);
    }

    [Fact]
    public void Cart_RejectsDuplicateAndForeignItems()
    {
        var product = CreateProduct(price: 10m, stock: 5);
        var cart = Cart.Create(Guid.NewGuid(), Guid.NewGuid());
        var item = cart.AddItem(Guid.NewGuid(), product, 1);
        var otherCart = Cart.Create(Guid.NewGuid(), Guid.NewGuid());

        var duplicate = Assert.Throws<DomainRuleViolationException>(() =>
            cart.AddItem(Guid.NewGuid(), product, 1));
        var foreignItem = Assert.Throws<DomainRuleViolationException>(() =>
            otherCart.RemoveItem(item));

        Assert.Equal("cart_item_duplicate", duplicate.Code);
        Assert.Equal("cart_item_not_owned", foreignItem.Code);
        Assert.Single(cart.CartItems);
        Assert.Empty(otherCart.CartItems);
    }

    [Fact]
    public void Cart_RejectsNewLineBeyondLimitWithoutMutation()
    {
        var cart = Cart.Create(Guid.NewGuid(), Guid.NewGuid());
        for (var index = 0; index < Cart.MaximumLineItems; index++)
        {
            cart.AddItem(
                Guid.NewGuid(),
                CreateProduct(price: 10m, stock: 1),
                1);
        }

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            cart.AddItem(
                Guid.NewGuid(),
                CreateProduct(price: 10m, stock: 1),
                1));

        Assert.Equal("cart_line_item_limit_exceeded", exception.Code);
        Assert.Equal(Cart.MaximumLineItems, cart.CartItems.Count);
    }

    [Fact]
    public void CartInvariantSetters_AreNotPublic()
    {
        AssertSetterIsNotPublic<Cart>(nameof(Cart.UserId));
        AssertSetterIsNotPublic<CartItem>(nameof(CartItem.CartId));
        AssertSetterIsNotPublic<CartItem>(nameof(CartItem.ProductId));
        AssertSetterIsNotPublic<CartItem>(nameof(CartItem.Quantity));
        AssertSetterIsNotPublic<CartItem>(nameof(CartItem.UnitPrice));
    }

    private static Product CreateProduct(decimal price, int stock)
        => Product.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Product",
            price,
            stock,
            "Description",
            DateTime.UtcNow);

    private static void AssertSetterIsNotPublic<TEntity>(string propertyName)
        => Assert.False(
            typeof(TEntity).GetProperty(propertyName)!.SetMethod!.IsPublic);
}
