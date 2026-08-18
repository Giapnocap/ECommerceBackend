using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface ICustomerManagementReadRepository
    {
        Task<bool> CustomerExistsAsync(
            Guid customerId,
            CancellationToken cancellationToken = default);

        Task<PageSlice<CustomerListItemResponse>> GetCustomersAsync(
            CustomerQueryParams queryParams,
            string? accountStatus,
            DateTime now,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task<CustomerDetailResponse?> GetCustomerDetailAsync(
            Guid customerId,
            DateTime now,
            CancellationToken cancellationToken = default);

        Task<PageSlice<CustomerOrderResponse>> GetOrdersAsync(
            Guid customerId,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task<PageSlice<CustomerReturnResponse>> GetReturnsAsync(
            Guid customerId,
            int skip,
            int take,
            CancellationToken cancellationToken = default);
    }
}
