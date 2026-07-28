using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Mappings;
using ECommerceBackend.Application.Validation;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Tests;

public class CartRulesTests
{
    [Fact]
    public void AddToCartValidator_RequiresPositiveQuantity()
    {
        var validator = new AddToCartRequestValidator();

        var result = validator.Validate(new AddToCartRequest
        {
            ProductId = Guid.NewGuid(),
            Quantity = 0
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(AddToCartRequest.Quantity));
    }

    [Fact]
    public void UpdateCartItemValidator_AllowsZeroForRemoval_ButRejectsNegativeQuantity()
    {
        var validator = new UpdateCartItemRequestValidator();

        Assert.True(validator.Validate(new UpdateCartItemRequest { Quantity = 0 }).IsValid);

        var negative = validator.Validate(new UpdateCartItemRequest { Quantity = -1 });
        Assert.False(negative.IsValid);
        Assert.Contains(negative.Errors, error => error.PropertyName == nameof(UpdateCartItemRequest.Quantity));
    }

    [Fact]
    public void CartMapping_UsesCurrentProductPrice_AndExcludesUnavailableItemsFromTotals()
    {
        var availableProduct = Product("Available", price: 12, stock: 5);
        var unavailableProduct = Product("Unavailable", price: 99, stock: 1);

        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            CartItems =
            {
                CartItem(availableProduct, quantity: 2, storedUnitPrice: 10),
                CartItem(unavailableProduct, quantity: 3, storedUnitPrice: 40)
            }
        };

        var response = cart.ToResponse();
        var items = response.Items.ToArray();

        Assert.Equal(2, response.TotalItems);
        Assert.Equal(24, response.TotalAmount);
        Assert.Equal(12, items.Single(item => item.ProductName == "Available").UnitPrice);
        Assert.False(items.Single(item => item.ProductName == "Unavailable").IsAvailable);
        Assert.Equal(1, items.Single(item => item.ProductName == "Unavailable").AvailableStock);
    }

    private static Product Product(string name, decimal price, int stock)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Price = price,
            StockQuantity = stock,
            Images =
            {
                new ProductImage
                {
                    Id = Guid.NewGuid(),
                    ImageUrl = $"/uploads/products/{name}.jpg",
                    IsMain = true
                }
            }
        };

    private static CartItem CartItem(Product product, int quantity, decimal storedUnitPrice)
        => new()
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Product = product,
            Quantity = quantity,
            UnitPrice = storedUnitPrice
        };
}
