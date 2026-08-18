using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Services
{
    public sealed class CustomerManagementService : ICustomerManagementService
    {
        private readonly ICustomerManagementReadRepository _customerRepository;
        private readonly IUserRepository _userRepository;
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IAuditWriter _audit;
        private readonly TimeProvider _timeProvider;

        public CustomerManagementService(
            ICustomerManagementReadRepository customerRepository,
            IUserRepository userRepository,
            IAuthSessionRepository authSessionRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            TimeProvider timeProvider,
            IAuditWriter? auditWriter = null)
        {
            _customerRepository = customerRepository;
            _userRepository = userRepository;
            _authSessionRepository = authSessionRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _timeProvider = timeProvider;
            _audit = auditWriter ?? NullAuditWriter.Instance;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<PagedResult<CustomerListItemResponse>> GetCustomersAsync(
            CustomerQueryParams query,
            CancellationToken cancellationToken = default)
        {
            var now = UtcNow;
            var status = NormalizeStatus(query.Status);
            DateTime? normalizedFrom = query.RegisteredFrom.HasValue
                ? NormalizeUtc(query.RegisteredFrom.Value)
                : null;
            DateTime? normalizedTo = query.RegisteredTo.HasValue
                ? NormalizeUtc(query.RegisteredTo.Value)
                : null;
            if (normalizedFrom.HasValue && normalizedTo.HasValue
                && normalizedFrom.Value >= normalizedTo.Value)
            {
                throw new BusinessException(
                    "customer_registered_range_invalid",
                    "Thời điểm bắt đầu phải nhỏ hơn thời điểm kết thúc.");
            }

            var readQuery = new CustomerQueryParams
            {
                Keyword = query.Keyword,
                Status = status,
                RegisteredFrom = normalizedFrom,
                RegisteredTo = normalizedTo,
                SortBy = query.SortBy,
                SortOrder = query.SortOrder,
                Page = query.Page,
                PageSize = query.PageSize
            };
            var paging = Paging.Normalize(query.Page, query.PageSize, defaultSize: 20);
            var result = await _customerRepository.GetCustomersAsync(
                readQuery,
                status,
                now,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);

            return PagedResult<CustomerListItemResponse>.Create(
                result.Items,
                result.TotalCount,
                paging.Page,
                paging.Size);
        }

        public async Task<CustomerDetailResponse> GetCustomerDetailAsync(
            Guid customerId,
            CancellationToken cancellationToken = default)
            => await _customerRepository.GetCustomerDetailAsync(
                customerId,
                UtcNow,
                cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy khách hàng.");

        public async Task<PagedResult<CustomerOrderResponse>> GetOrdersAsync(
            Guid customerId,
            CustomerPageQueryParams query,
            CancellationToken cancellationToken = default)
        {
            await EnsureCustomerExistsAsync(customerId, cancellationToken);
            var paging = Paging.Normalize(query.Page, query.PageSize, defaultSize: 20);
            var result = await _customerRepository.GetOrdersAsync(
                customerId,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);

            return PagedResult<CustomerOrderResponse>.Create(
                result.Items,
                result.TotalCount,
                paging.Page,
                paging.Size);
        }

        public async Task<PagedResult<CustomerReturnResponse>> GetReturnsAsync(
            Guid customerId,
            CustomerPageQueryParams query,
            CancellationToken cancellationToken = default)
        {
            await EnsureCustomerExistsAsync(customerId, cancellationToken);
            var paging = Paging.Normalize(query.Page, query.PageSize, defaultSize: 20);
            var result = await _customerRepository.GetReturnsAsync(
                customerId,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);

            return PagedResult<CustomerReturnResponse>.Create(
                result.Items,
                result.TotalCount,
                paging.Page,
                paging.Size);
        }

        public Task<CustomerAccountStatusResponse> LockAsync(
            Guid actorUserId,
            Guid customerId,
            CancellationToken cancellationToken = default)
            => ChangeLockStateAsync(
                actorUserId,
                customerId,
                shouldLock: true,
                cancellationToken);

        public Task<CustomerAccountStatusResponse> UnlockAsync(
            Guid actorUserId,
            Guid customerId,
            CancellationToken cancellationToken = default)
            => ChangeLockStateAsync(
                actorUserId,
                customerId,
                shouldLock: false,
                cancellationToken);

        private async Task<CustomerAccountStatusResponse> ChangeLockStateAsync(
            Guid actorUserId,
            Guid customerId,
            bool shouldLock,
            CancellationToken cancellationToken)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            try
            {
                var customer = await _consistency.LockUserAsync(
                    customerId,
                    activeOnly: true,
                    cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy khách hàng.");
                await _userRepository.LoadRolesAsync(
                    customer,
                    includePermissions: false,
                    cancellationToken);
                if (!customer.UserRoles.Any(
                        assignment => assignment.Role?.Name == RoleNames.Customer))
                {
                    throw new NotFoundException("Không tìm thấy khách hàng.");
                }

                var changed = shouldLock
                    ? DomainRuleGuard.AsConflict(customer.LockByAdministrator)
                    : DomainRuleGuard.AsConflict(customer.UnlockByAdministrator);
                var now = UtcNow;
                if (changed)
                {
                    await RevokeAllRefreshTokensAsync(
                        customer.Id,
                        shouldLock ? "Customer locked by administrator" : "Customer unlocked by administrator",
                        now,
                        cancellationToken);
                    _audit.Write(
                        shouldLock ? "customer.lock" : "customer.unlock",
                        "User",
                        customer.Id.ToString(),
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["accountStatus"] = shouldLock ? "Locked" : "Active"
                        });
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return new CustomerAccountStatusResponse
                {
                    CustomerId = customer.Id,
                    AccountStatus = customer.IsLockedOutAt(now) ? "Locked" : "Active",
                    LockedUntil = customer.IsLockedOutAt(now)
                        ? customer.LockoutEndAt
                        : null,
                    Changed = changed
                };
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "customer_account_concurrency_conflict",
                    "Tài khoản khách hàng vừa được cập nhật bởi yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "customer_account_concurrency_conflict",
                    "Tài khoản khách hàng đang được cập nhật. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        private async Task EnsureCustomerExistsAsync(
            Guid customerId,
            CancellationToken cancellationToken)
        {
            if (!await _customerRepository.CustomerExistsAsync(
                    customerId,
                    cancellationToken))
            {
                throw new NotFoundException("Không tìm thấy khách hàng.");
            }
        }

        private async Task RevokeAllRefreshTokensAsync(
            Guid customerId,
            string reason,
            DateTime occurredAt,
            CancellationToken cancellationToken)
        {
            var tokens = await _authSessionRepository.GetActiveRefreshTokensAsync(
                customerId,
                cancellationToken);
            foreach (var token in tokens)
            {
                DomainRuleGuard.AsConflict(() => token.Revoke(occurredAt, reason));
            }
        }

        private static string? NormalizeStatus(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim().ToLowerInvariant() switch
            {
                "active" => "active",
                "locked" => "locked",
                _ => throw new BusinessException(
                    "customer_status_invalid",
                    "Trạng thái khách hàng phải là active hoặc locked.")
            };
        }

        private static DateTime NormalizeUtc(DateTime value)
            => value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
    }
}
