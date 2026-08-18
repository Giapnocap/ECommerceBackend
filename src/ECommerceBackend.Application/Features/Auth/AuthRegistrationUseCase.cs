using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Observability;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuthRegistrationUseCase
    {
        private readonly IUserRepository _userRepository;
        private readonly ICartRepository _cartRepository;
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IPasswordHasher _passwordHasher;
        private readonly AuthTokenIssuer _tokenIssuer;
        private readonly EmailVerificationUseCase _emailVerification;
        private readonly TimeProvider _timeProvider;

        public AuthRegistrationUseCase(
            IUserRepository userRepository,
            ICartRepository cartRepository,
            IAuthSessionRepository authSessionRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IPasswordHasher passwordHasher,
            AuthTokenIssuer tokenIssuer,
            EmailVerificationUseCase emailVerification,
            TimeProvider timeProvider)
        {
            _userRepository = userRepository;
            _cartRepository = cartRepository;
            _authSessionRepository = authSessionRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _passwordHasher = passwordHasher;
            _tokenIssuer = tokenIssuer;
            _emailVerification = emailVerification;
            _timeProvider = timeProvider;
        }

        public async Task<AuthResponse> ExecuteAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start(
                "auth.register",
                cancellationToken);
            var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
            var userName = request.UserName.Trim();
            var email = request.Email.Trim();
            var normalizedUserName = Normalize(userName);
            var normalizedEmail = Normalize(email);

            if (await _userRepository.UserNameExistsAsync(
                normalizedUserName,
                cancellationToken))
            {
                throw new ConflictException(
                    "username_conflict",
                    $"Tên đăng nhập '{userName}' đã tồn tại.");
            }

            if (await _userRepository.EmailExistsAsync(
                normalizedEmail,
                cancellationToken))
            {
                throw new ConflictException(
                    "email_conflict",
                    $"Email '{email}' đã được sử dụng.");
            }

            var customerRole = await _userRepository.GetRoleAsync(
                RoleNames.Customer,
                includePermissions: true,
                cancellationToken)
                ?? throw new NotFoundException(
                    "Không tìm thấy vai trò khách hàng. "
                    + "Hãy áp dụng bản cập nhật cơ sở dữ liệu.");
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                NormalizedUserName = normalizedUserName,
                Email = email,
                NormalizedEmail = normalizedEmail,
                FullName = request.FullName.Trim(),
                Phone = NormalizeOptional(request.Phone),
                PasswordHash = _passwordHasher.Hash(request.Password),
                CreatedAt = occurredAt
            };
            await _userRepository.AddAsync(user, cancellationToken);
            await _userRepository.AddRoleAsync(
                UserRole.Create(user.Id, customerRole),
                cancellationToken);
            await _cartRepository.AddAsync(
                Cart.Create(Guid.NewGuid(), user.Id),
                cancellationToken);
            var refreshToken = _tokenIssuer.CreateRefreshToken(
                user.Id,
                Guid.NewGuid(),
                occurredAt);
            await _authSessionRepository.AddRefreshTokenAsync(
                refreshToken.Entity,
                cancellationToken);
            _emailVerification.IssueForRegistration(user, occurredAt);

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
                when (_consistency.IsUniqueConstraintViolation(ex))
            {
                throw new ConflictException(
                    "identity_conflict",
                    "Tên đăng nhập hoặc email đã được sử dụng "
                    + "bởi một yêu cầu khác.",
                    ex);
            }

            var response = _tokenIssuer.BuildResponse(
                user,
                [customerRole.Name],
                customerRole.RolePermissions
                    .Where(item => item.Permission != null)
                    .Select(item => item.Permission!.Name),
                refreshToken.RawToken,
                refreshToken.Entity.ExpiresAt,
                refreshToken.Entity.FamilyId,
                occurredAt);
            telemetry.Complete();
            return response;
        }

        private static string Normalize(string value)
            => value.Trim().ToUpperInvariant();

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
