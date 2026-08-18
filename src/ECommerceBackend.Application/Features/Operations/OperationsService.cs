using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;

namespace ECommerceBackend.Application.Services
{
    public sealed class OperationsService : IOperationsService
    {
        private readonly DeadLetterUseCase _deadLetters;
        private readonly AuditQueryUseCase _auditQuery;
        private readonly DataRetentionUseCase _dataRetention;

        public OperationsService(
            DeadLetterUseCase deadLetters,
            AuditQueryUseCase auditQuery,
            DataRetentionUseCase dataRetention)
        {
            _deadLetters = deadLetters;
            _auditQuery = auditQuery;
            _dataRetention = dataRetention;
        }

        public Task<PagedResult<DeadLetterResponse>> GetDeadLettersAsync(
            DeadLetterQueryParams query,
            CancellationToken cancellationToken = default)
            => _deadLetters.GetAsync(query, cancellationToken);

        public Task<RedriveOutboxResponse> RedriveDeadLetterAsync(
            Guid messageId,
            Guid actorUserId,
            CancellationToken cancellationToken = default)
            => _deadLetters.RedriveAsync(
                messageId,
                actorUserId,
                cancellationToken);

        public Task<PagedResult<AuditEventResponse>> GetAuditEventsAsync(
            AuditQueryParams query,
            CancellationToken cancellationToken = default)
            => _auditQuery.ExecuteAsync(query, cancellationToken);

        public Task<AuditEventResponse> GetAuditEventAsync(
            Guid id,
            CancellationToken cancellationToken = default)
            => _auditQuery.GetByIdAsync(id, cancellationToken);

        public Task<DataRetentionResponse> RunDataRetentionAsync(
            DataRetentionRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken = default)
            => _dataRetention.ExecuteAsync(
                request,
                actorUserId,
                cancellationToken);
    }
}
