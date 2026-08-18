using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Interfaces
{
    public sealed record GatewayPaymentCreationRequest(
        Guid PaymentId,
        Guid OrderId,
        string OrderNumber,
        decimal Amount,
        string Currency,
        string IdempotencyKey);

    public sealed record GatewayPaymentCreationResult(
        string ProviderPaymentId,
        string? ClientSecret,
        PaymentStatus Status);

    public sealed record GatewayPaymentStatusResult(
        string ProviderPaymentId,
        decimal Amount,
        string Currency,
        PaymentStatus Status);

    public sealed record GatewayRefundRequest(
        Guid PaymentId,
        string ProviderPaymentId,
        decimal Amount,
        string Currency,
        string IdempotencyKey);

    public enum GatewayRefundStatus
    {
        Pending = 0,
        Succeeded = 1,
        Failed = 2,
        Cancelled = 3
    }

    public sealed record GatewayRefundResult(
        string ProviderRefundId,
        decimal Amount,
        GatewayRefundStatus Status);

    public interface IPaymentGateway
    {
        string ProviderCode { get; }

        Task<GatewayPaymentCreationResult> CreatePaymentAsync(
            GatewayPaymentCreationRequest request,
            CancellationToken cancellationToken = default);

        Task<GatewayPaymentStatusResult> GetPaymentAsync(
            string providerPaymentId,
            CancellationToken cancellationToken = default);

        Task<GatewayRefundResult> RefundAsync(
            GatewayRefundRequest request,
            CancellationToken cancellationToken = default);
    }
}
