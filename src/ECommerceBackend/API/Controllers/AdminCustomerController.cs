using System.Security.Claims;
using Asp.Versioning;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
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

        [HttpGet]
        [ProducesResponseType(typeof(PagedResult<CustomerListItemResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCustomers(
            [FromQuery] CustomerQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _customerService.GetCustomersAsync(query, cancellationToken));

        [HttpGet("{customerId:guid}")]
        [ProducesResponseType(typeof(CustomerDetailResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCustomer(
            Guid customerId,
            CancellationToken cancellationToken)
            => Ok(await _customerService.GetCustomerDetailAsync(customerId, cancellationToken));

        [HttpGet("{customerId:guid}/orders")]
        [ProducesResponseType(typeof(PagedResult<CustomerOrderResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetOrders(
            Guid customerId,
            [FromQuery] CustomerPageQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _customerService.GetOrdersAsync(customerId, query, cancellationToken));

        [HttpGet("{customerId:guid}/returns")]
        [ProducesResponseType(typeof(PagedResult<CustomerReturnResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetReturns(
            Guid customerId,
            [FromQuery] CustomerPageQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _customerService.GetReturnsAsync(customerId, query, cancellationToken));

        [HttpPost("{customerId:guid}/lock")]
        [ProducesResponseType(typeof(CustomerAccountStatusResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Lock(
            Guid customerId,
            CancellationToken cancellationToken)
            => Ok(await _customerService.LockAsync(
                CurrentUserId,
                customerId,
                cancellationToken));

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
