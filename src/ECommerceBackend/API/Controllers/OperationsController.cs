using System.Security.Claims;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Các thao tác khôi phục, nhật ký và đối soát dành cho quản trị viên</summary>
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

        /// <summary>List outbox messages that exhausted delivery retries</summary>
        [HttpGet("outbox/dead-letters")]
        [ProducesResponseType(typeof(PagedResult<DeadLetterResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetDeadLetters(
            [FromQuery] DeadLetterQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _operations.GetDeadLettersAsync(query, cancellationToken));

        /// <summary>Return a dead-lettered outbox message to the delivery queue</summary>
        [HttpPost("outbox/dead-letters/{id:guid}/redrive")]
        [ProducesResponseType(typeof(RedriveOutboxResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RedriveDeadLetter(
            Guid id,
            CancellationToken cancellationToken)
            => Ok(await _operations.RedriveDeadLetterAsync(id, CurrentUserId, cancellationToken));

        /// <summary>Tra cứu nhật ký bảo mật và nghiệp vụ</summary>
        [HttpGet("audit-events")]
        [ProducesResponseType(typeof(PagedResult<AuditEventResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAuditEvents(
            [FromQuery] AuditQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _operations.GetAuditEventsAsync(query, cancellationToken));

        /// <summary>Phát hiện hoặc xử lý chênh lệch giữa tệp tải lên và dữ liệu trong cơ sở dữ liệu</summary>
        [HttpPost("uploads/reconcile")]
        [ProducesResponseType(typeof(UploadReconciliationResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ReconcileUploads(
            [FromBody] UploadReconciliationRequest request,
            CancellationToken cancellationToken)
            => Ok(await _uploadReconciliation.ReconcileAsync(
                request,
                CurrentUserId,
                cancellationToken));

        /// <summary>Xem trước hoặc áp dụng chính sách lưu giữ dữ liệu vận hành</summary>
        [HttpPost("data-retention")]
        [ProducesResponseType(typeof(DataRetentionResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RunDataRetention(
            [FromBody] DataRetentionRequest request,
            CancellationToken cancellationToken)
            => Ok(await _operations.RunDataRetentionAsync(
                request,
                CurrentUserId,
                cancellationToken));
    }
}
