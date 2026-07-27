using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Validation;
using ECommerceBackend.Domain.Enums;
using FluentValidation.Results;

namespace ECommerceBackend.Tests;

public sealed class ValidationContractTests
{
    [Theory]
    [MemberData(nameof(InvalidRequestCases))]
    public void Validators_RejectInvalidInput(string _, Func<ValidationResult> validate)
    {
        Assert.False(validate().IsValid);
    }

    public static IEnumerable<object[]> InvalidRequestCases()
    {
        yield return Case("cart product", () => new AddToCartRequestValidator().Validate(
            new AddToCartRequest { ProductId = Guid.Empty, Quantity = 0 }));
        yield return Case("cart quantity", () => new UpdateCartItemRequestValidator().Validate(
            new UpdateCartItemRequest { Quantity = -1 }));
        yield return Case("checkout", () => new PlaceOrderRequestValidator().Validate(
            new PlaceOrderRequest
            {
                ShippingAddress = string.Empty,
                PaymentMethod = (PaymentMethod)999,
                ShippingMethod = (ShippingMethod)999,
                PromotionCode = "?"
            }));
        yield return Case("order quote", () => new OrderQuoteRequestValidator().Validate(
            new OrderQuoteRequest
            {
                ShippingMethod = (ShippingMethod)999,
                PromotionCode = "?"
            }));
        yield return Case("promotion create", () => new CreatePromotionRequestValidator().Validate(
            new CreatePromotionRequest
            {
                Code = "?",
                Type = (PromotionType)999,
                Value = 0,
                StartsAt = DateTime.UtcNow,
                EndsAt = DateTime.UtcNow.AddMinutes(-1),
                UsageLimit = 0,
                UsageLimitPerCustomer = 0
            }));
        yield return Case("promotion query", () => new PromotionQueryParamsValidator().Validate(
            new PromotionQueryParams { Page = 0, PageSize = 101 }));
        yield return Case("category", () => new CreateCategoryRequestValidator().Validate(
            new CreateCategoryRequest { Name = string.Empty }));
        yield return Case("category update", () => new UpdateCategoryRequestValidator().Validate(
            new UpdateCategoryRequest { Name = new string('a', 101) }));
        yield return Case("profile", () => new UpdateProfileRequestValidator().Validate(
            new UpdateProfileRequest { FullName = string.Empty, Phone = "invalid" }));
        yield return Case("password", () => new ChangePasswordRequestValidator().Validate(
            new ChangePasswordRequest { CurrentPassword = string.Empty, NewPassword = "short" }));
        yield return Case("forgot password", () => new ForgotPasswordRequestValidator().Validate(
            new ForgotPasswordRequest { Email = "invalid-email" }));
        yield return Case("reset password", () => new ResetPasswordRequestValidator().Validate(
            new ResetPasswordRequest { Token = string.Empty, NewPassword = "short" }));
        yield return Case("role", () => new AssignRoleRequestValidator().Validate(
            new AssignRoleRequest { RoleName = "owner" }));
        yield return Case("users", () => new UserQueryParamsValidator().Validate(
            new UserQueryParams { Page = 0, PageSize = 101, Role = "owner" }));
        yield return Case("order status", () => new UpdateOrderStatusRequestValidator().Validate(
            new UpdateOrderStatusRequest
            {
                Status = (OrderStatus)99,
                Note = new string('a', 501)
            }));
        yield return Case("order cancellation", () => new CancelOrderRequestValidator().Validate(
            new CancelOrderRequest { Reason = new string('a', 201) }));
        yield return Case("order query", () => new OrderQueryParamsValidator().Validate(
            new OrderQueryParams { Page = 0, PageSize = 101 }));
        yield return Case("inventory query", () => new InventoryQueryParamsValidator().Validate(
            new InventoryQueryParams { Page = 0, PageSize = 101 }));
        yield return Case("low stock query", () => new LowStockQueryParamsValidator().Validate(
            new LowStockQueryParams { Threshold = -1 }));
        yield return Case("sales summary", () => new SalesSummaryQueryValidator().Validate(
            new SalesSummaryQuery
            {
                From = DateTime.UtcNow,
                To = DateTime.UtcNow.AddDays(-1),
                LowStockThreshold = -1,
                TopProductLimit = 0
            }));
        yield return Case("dead letter query", () => new DeadLetterQueryParamsValidator().Validate(
            new DeadLetterQueryParams { Page = 0, PageSize = 101 }));
        yield return Case("audit query", () => new AuditQueryParamsValidator().Validate(
            new AuditQueryParams
            {
                Action = new string('a', 101),
                EntityType = new string('b', 101),
                From = DateTime.UtcNow,
                To = DateTime.UtcNow.AddDays(-1)
            }));
        yield return Case("upload reconciliation", () => new UploadReconciliationRequestValidator().Validate(
            new UploadReconciliationRequest { MaxDeletes = 0 }));
        yield return Case("data retention", () => new DataRetentionRequestValidator().Validate(
            new DataRetentionRequest { MaxBatchSize = 0 }));
        yield return Case("product query", () => new ProductQueryParamsValidator().Validate(
            new ProductQueryParams
            {
                MinPrice = 10,
                MaxPrice = 9,
                SortBy = "unknown",
                SortOrder = "sideways",
                Page = 0,
                PageSize = 101
            }));
        yield return Case("product create", () => new CreateProductRequestValidator().Validate(
            new CreateProductRequest
            {
                Name = string.Empty,
                Price = 0,
                StockQuantity = -1,
                CategoryId = Guid.Empty,
                Description = new string('a', 2001)
            }));
        yield return Case("product update", () => new UpdateProductRequestValidator().Validate(
            new UpdateProductRequest
            {
                Name = string.Empty,
                Price = CommerceLimits.MaxMoneyAmount + 1,
                StockQuantity = -1,
                CategoryId = Guid.Empty,
                Description = new string('a', 2001)
            }));
    }

    private static object[] Case(string name, Func<ValidationResult> validate)
        => [name, validate];
}
