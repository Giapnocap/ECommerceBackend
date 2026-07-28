using System.Data;
using System.Diagnostics.Metrics;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;

namespace ECommerceBackend.Application.Services
{
    public sealed class DeadLetterUseCase
    {
        private static readonly Meter Meter =
            new("ECommerceBackend.Operations");
        private static readonly Counter<long> RedriveCounter =
            Meter.CreateCounter<long>("outbox.dead_letters.redriven");
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IAuditWriter _audit;
        private readonly TimeProvider _timeProvider;

        public DeadLetterUseCase(
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IAuditWriter audit,
            TimeProvider timeProvider)
        {
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _audit = audit;
            _timeProvider = timeProvider;
        }

        public async Task<PagedResult<DeadLetterResponse>> GetAsync(
            DeadLetterQueryParams query,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(
                query.Page,
                query.PageSize,
                defaultSize: 20);
            var page = await _outboxRepository.GetDeadLettersAsync(
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);
            return PagedResult<DeadLetterResponse>.Create(
                page.Items,
                page.TotalCount,
                paging.Page,
                paging.Size);
        }

        public async Task<RedriveOutboxResponse> RedriveAsync(
            Guid messageId,
            Guid actorUserId,
            CancellationToken cancellationToken = default)
        {
            await using var transaction =
                await _consistency.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            var completed = false;
            try
            {
                var message = await _consistency.LockOutboxMessageAsync(
                    messageId,
                    cancellationToken)
                    ?? throw new NotFoundException(
                        "Không tìm thấy thông báo trong hàng đợi.");
                if (message.ProcessedAt.HasValue)
                {
                    throw new ConflictException(
                        "Không thể gửi lại thông báo đã được xử lý thành công.");
                }

                if (!message.DeadLetteredAt.HasValue)
                {
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                    return new RedriveOutboxResponse
                    {
                        Id = message.Id,
                        ReDriven = false,
                        NextAttemptAt = message.NextAttemptAt
                    };
                }

                var previousAttempts = message.Attempts;
                var previousDeadLetteredAt = message.DeadLetteredAt;
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                message.Attempts = 0;
                message.NextAttemptAt = now;
                message.DeadLetteredAt = null;
                message.LockId = null;
                message.LockedAt = null;
                message.LastError = null;
                _audit.Write(
                    "outbox.dead_letter.redrive",
                    "OutboxMessage",
                    message.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?>
                    {
                        ["previousAttempts"] = previousAttempts,
                        ["previousDeadLetteredAt"] =
                            previousDeadLetteredAt,
                        ["messageType"] = message.Type
                    });

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                completed = true;
                RedriveCounter.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        "message.type",
                        message.Type));
                return new RedriveOutboxResponse
                {
                    Id = message.Id,
                    ReDriven = true,
                    NextAttemptAt = now
                };
            }
            catch
            {
                if (!completed)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }
}
