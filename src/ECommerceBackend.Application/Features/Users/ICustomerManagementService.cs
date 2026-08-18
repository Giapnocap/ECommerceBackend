using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface ICustomerManagementService
    {
        Task<PagedResult<CustomerListItemResponse>> GetCustomersAsync(
            CustomerQueryParams query,
            CancellationToken cancellationToken = default);

        Task<CustomerDetailResponse> GetCustomerDetailAsync(
            Guid customerId,
            CancellationToken cancellationToken = default);

        Task<PagedResult<CustomerOrderResponse>> GetOrdersAsync(
            Guid customerId,
            CustomerPageQueryParams query,
            CancellationToken cancellationToken = default);

        Task<PagedResult<CustomerReturnResponse>> GetReturnsAsync(
            Guid customerId,
            CustomerPageQueryParams query,
            CancellationToken cancellationToken = default);

        Task<CustomerAccountStatusResponse> LockAsync(
            Guid actorUserId,
            Guid customerId,
            CancellationToken cancellationToken = default);

        Task<CustomerAccountStatusResponse> UnlockAsync(
            Guid actorUserId,
            Guid customerId,
            CancellationToken cancellationToken = default);
    }
}
