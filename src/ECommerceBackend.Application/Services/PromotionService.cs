using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Mappings;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Services
{
    public sealed class PromotionService : IPromotionService
    {
        private readonly IPromotionRepository _promotionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IAuditWriter _audit;
        private readonly TimeProvider _timeProvider;

        public PromotionService(
            IPromotionRepository promotionRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IAuditWriter audit,
            TimeProvider timeProvider)
        {
            _promotionRepository = promotionRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _audit = audit;
            _timeProvider = timeProvider;
        }

        public async Task<PagedResult<PromotionResponse>> GetAllAsync(
            PromotionQueryParams query,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(
                query.Page,
                query.PageSize);
            var page = await _promotionRepository.GetPageAsync(
                query.IsActive,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);
            return PagedResult<PromotionResponse>.Create(
                page.Items.Select(promotion => promotion.ToResponse()),
                page.TotalCount,
                paging.Page,
                paging.Size);
        }

        public async Task<PromotionResponse> GetByIdAsync(
            Guid promotionId,
            CancellationToken cancellationToken = default)
        {
            var promotion = await _promotionRepository.GetByIdAsync(
                promotionId,
                cancellationToken)
                ?? throw PromotionNotFound(promotionId);
            return promotion.ToResponse();
        }

        public async Task<PromotionResponse> CreateAsync(
            CreatePromotionRequest request,
            Guid actorUserId,
            CancellationToken cancellationToken = default)
        {
            var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
            var promotion = DomainRuleGuard.AsBusiness(() =>
                Promotion.Create(
                    Guid.NewGuid(),
                    request.Code,
                    request.Type,
                    request.Value,
                    request.MinimumSubtotal,
                    request.MaximumDiscountAmount,
                    request.StartsAt,
                    request.EndsAt,
                    request.UsageLimit,
                    request.UsageLimitPerCustomer,
                    occurredAt));
            await _promotionRepository.AddAsync(
                promotion,
                cancellationToken);
            _audit.Write(
                "promotion.create",
                "Promotion",
                promotion.Id.ToString(),
                actorUserId,
                AuditMetadata(promotion));

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
                when (_consistency.IsUniqueConstraintViolation(ex))
            {
                throw new ConflictException(
                    "promotion_code_exists",
                    $"Mã khuyến mãi '{promotion.Code}' đã tồn tại.",
                    ex);
            }

            return promotion.ToResponse();
        }

        public async Task<PromotionResponse> UpdateAsync(
            Guid promotionId,
            UpdatePromotionRequest request,
            Guid actorUserId,
            CancellationToken cancellationToken = default)
        {
            await using var transaction =
                await _consistency.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            try
            {
                var promotion =
                    await _promotionRepository.LockByIdAsync(
                        promotionId,
                        cancellationToken)
                    ?? throw PromotionNotFound(promotionId);
                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                DomainRuleGuard.AsBusiness(() =>
                    promotion.Update(
                        request.Type,
                        request.Value,
                        request.MinimumSubtotal,
                        request.MaximumDiscountAmount,
                        request.StartsAt,
                        request.EndsAt,
                        request.UsageLimit,
                        request.UsageLimitPerCustomer,
                        request.IsActive,
                        occurredAt));
                _audit.Write(
                    "promotion.update",
                    "Promotion",
                    promotion.Id.ToString(),
                    actorUserId,
                    AuditMetadata(promotion));
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return promotion.ToResponse();
            }
            catch (Exception ex)
                when (_consistency.IsConcurrencyConflict(ex))
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);
                throw new ConflictException(
                    "promotion_concurrency_conflict",
                    "Mã khuyến mãi vừa được cập nhật bởi yêu cầu khác.",
                    ex);
            }
            catch
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);
                throw;
            }
        }

        public async Task DeactivateAsync(
            Guid promotionId,
            Guid actorUserId,
            CancellationToken cancellationToken = default)
        {
            await using var transaction =
                await _consistency.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            try
            {
                var promotion =
                    await _promotionRepository.LockByIdAsync(
                        promotionId,
                        cancellationToken)
                    ?? throw PromotionNotFound(promotionId);
                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                var changed = DomainRuleGuard.AsBusiness(() =>
                    promotion.Deactivate(occurredAt));
                if (changed)
                {
                    _audit.Write(
                        "promotion.deactivate",
                        "Promotion",
                        promotion.Id.ToString(),
                        actorUserId);
                    await _unitOfWork.SaveChangesAsync(
                        cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
                when (_consistency.IsConcurrencyConflict(ex))
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);
                throw new ConflictException(
                    "promotion_concurrency_conflict",
                    "Mã khuyến mãi vừa được cập nhật bởi yêu cầu khác.",
                    ex);
            }
            catch
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);
                throw;
            }
        }

        private static NotFoundException PromotionNotFound(Guid id)
            => new($"Không tìm thấy mã khuyến mãi với Id '{id}'.");

        private static Dictionary<string, object?> AuditMetadata(
            Promotion promotion)
            => new()
            {
                ["code"] = promotion.Code,
                ["type"] = promotion.Type.ToString(),
                ["isActive"] = promotion.IsActive,
                ["usageLimit"] = promotion.UsageLimit
            };
    }
}
