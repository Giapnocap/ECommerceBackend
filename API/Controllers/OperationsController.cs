using System.Security.Claims;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    [ApiController]
    [Route("api/operations")]
    [Authorize(Roles = RoleNames.Admin)]
    [Produces("application/json")]
    public sealed class OperationsController : ControllerBase
    {
        private readonly IOperationsService _operations;
        private readonly IUploadReconciliationService _uploadReconciliation;

        public OperationsController(
            IOperationsService operations,
            IUploadReconciliationService uploadReconciliation)
        {
            _operations = operations;
            _uploadReconciliation = uploadReconciliation;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet("outbox/dead-letters")]
        [ProducesResponseType(typeof(PagedResult<DeadLetterResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetDeadLetters(
            [FromQuery] DeadLetterQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _operations.GetDeadLettersAsync(query, cancellationToken));

        [HttpPost("outbox/dead-letters/{id:guid}/redrive")]
        [ProducesResponseType(typeof(RedriveOutboxResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RedriveDeadLetter(
            Guid id,
            CancellationToken cancellationToken)
            => Ok(await _operations.RedriveDeadLetterAsync(id, CurrentUserId, cancellationToken));

        [HttpGet("audit-events")]
        [ProducesResponseType(typeof(PagedResult<AuditEventResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAuditEvents(
            [FromQuery] AuditQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _operations.GetAuditEventsAsync(query, cancellationToken));

        [HttpPost("uploads/reconcile")]
        [ProducesResponseType(typeof(UploadReconciliationResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ReconcileUploads(
            [FromBody] UploadReconciliationRequest request,
            CancellationToken cancellationToken)
            => Ok(await _uploadReconciliation.ReconcileAsync(
                request,
                CurrentUserId,
                cancellationToken));
    }
}
