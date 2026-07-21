using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IReportService
    {
        Task<SalesSummaryResponse> GetSalesSummaryAsync(
            SalesSummaryQuery query,
            CancellationToken cancellationToken = default);
    }
}
