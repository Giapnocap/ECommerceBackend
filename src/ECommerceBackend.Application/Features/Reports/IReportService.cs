using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IReportService
    {
        Task<SalesSummaryResponse> GetSalesSummaryAsync(
            SalesSummaryQuery query,
            CancellationToken cancellationToken = default);

        Task<RevenueReportResponse> GetRevenueReportAsync(
            RevenueReportQuery query,
            CancellationToken cancellationToken = default);

        Task<OrderReportResponse> GetOrderReportAsync(
            OrderReportQuery query,
            CancellationToken cancellationToken = default);

        Task<ProductReportResponse> GetProductReportAsync(
            ProductReportQuery query,
            CancellationToken cancellationToken = default);

        Task<CustomerReportResponse> GetCustomerReportAsync(
            CustomerReportQuery query,
            CancellationToken cancellationToken = default);

        Task<ReturnReportResponse> GetReturnReportAsync(
            ReturnReportQuery query,
            CancellationToken cancellationToken = default);
    }
}
