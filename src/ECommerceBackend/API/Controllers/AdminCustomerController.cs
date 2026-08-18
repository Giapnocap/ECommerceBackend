using System.Security.Claims;
using Asp.Versioning;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Quản lý khách hàng dành cho quản trị viên</summary>
    [ApiController]
    [ApiVersion(1.0)]
    [Route("api/admin/customers")]
    [Route("api/v{version:apiVersion}/admin/customers")]
    [Authorize(Policy = PermissionNames.ManageUsers)]
    [Produces("application/json")]
    public sealed class AdminCustomerController : ControllerBase
    {
        private readonly ICustomerManagementService _customerService;

        public AdminCustomerController(ICustomerManagementService customerService)
        {
            _customerService = customerService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        /// <summary>Lấy danh sách khách hàng có tìm kiếm, lọc trạng thái và phân trang</summary>
        [HttpGet]
        [ProducesResponseType(typeof(PagedResult<CustomerListItemResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCustomers(
            [FromQuery] CustomerQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _customerService.GetCustomersAsync(query, cancellationToken));

        /// <summary>Lấy hồ sơ và các chỉ số tổng hợp của khách hàng</summary>
        [HttpGet("{customerId:guid}")]
        [ProducesResponseType(typeof(CustomerDetailResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCustomer(
            Guid customerId,
            CancellationToken cancellationToken)
            => Ok(await _customerService.GetCustomerDetailAsync(customerId, cancellationToken));

        /// <summary>Lấy đơn hàng của một khách hàng theo trang</summary>
        [HttpGet("{customerId:guid}/orders")]
        [ProducesResponseType(typeof(PagedResult<CustomerOrderResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetOrders(
            Guid customerId,
            [FromQuery] CustomerPageQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _customerService.GetOrdersAsync(customerId, query, cancellationToken));

        /// <summary>Lấy yêu cầu trả hàng của một khách hàng theo trang</summary>
        [HttpGet("{customerId:guid}/returns")]
        [ProducesResponseType(typeof(PagedResult<CustomerReturnResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetReturns(
            Guid customerId,
            [FromQuery] CustomerPageQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _customerService.GetReturnsAsync(customerId, query, cancellationToken));

        /// <summary>Khóa khách hàng và thu hồi toàn bộ phiên đăng nhập đang hoạt động</summary>
        [HttpPost("{customerId:guid}/lock")]
        [ProducesResponseType(typeof(CustomerAccountStatusResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Lock(
            Guid customerId,
            CancellationToken cancellationToken)
            => Ok(await _customerService.LockAsync(
                CurrentUserId,
                customerId,
                cancellationToken));

        /// <summary>Mở khóa khách hàng; token cũ vẫn không được khôi phục</summary>
        [HttpPost("{customerId:guid}/unlock")]
        [ProducesResponseType(typeof(CustomerAccountStatusResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Unlock(
            Guid customerId,
            CancellationToken cancellationToken)
            => Ok(await _customerService.UnlockAsync(
                CurrentUserId,
                customerId,
                cancellationToken));
    }
}
