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
    [Route("api/v{version:apiVersion}/operations")]
    [Authorize(Roles = RoleNames.Admin)]
    [Produces("application/json")]
    public sealed class OperationsController : ControllerBase
    {
        private readonly IOperationsService _operations;

        public OperationsController(IOperationsService operations)
        {
            _operations = operations;
        }

        [HttpGet("audit-events")]
        [ProducesResponseType(typeof(PagedResult<AuditEventResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAuditEvents(
            [FromQuery] AuditQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _operations.GetAuditEventsAsync(query, cancellationToken));

        [HttpGet("audit-events/{id:guid}")]
        [ProducesResponseType(typeof(AuditEventResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAuditEvent(
            Guid id,
            CancellationToken cancellationToken)
            => Ok(await _operations.GetAuditEventAsync(id, cancellationToken));
    }
}
