using System.Security.Cryptography;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuthTokenIssuer
    {
        private readonly JwtOptions _options;
        private readonly IAccessTokenGenerator _accessTokenGenerator;

        public AuthTokenIssuer(
            IOptions<JwtOptions> options,
            IAccessTokenGenerator accessTokenGenerator)
        {
            _options = options.Value;
            _accessTokenGenerator = accessTokenGenerator;
        }

        public RefreshTokenIssue CreateRefreshToken(
            Guid userId,
            Guid familyId,
            DateTime occurredAt)
        {
            var rawToken = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(64));
            var expiresAt = occurredAt.AddDays(_options.RefreshTokenDays);

            return new RefreshTokenIssue(
                rawToken,
                DomainRuleGuard.AsBusiness(() =>
                    RefreshToken.Create(
                        Guid.NewGuid(),
                        userId,
                        familyId,
                        HashRefreshToken(rawToken),
                        occurredAt,
                        expiresAt)));
        }

        public AuthResponse BuildResponse(
            User user,
            IEnumerable<string> roles,
            IEnumerable<string> permissions,
            string refreshToken,
            DateTime refreshTokenExpiresAt,
            Guid sessionId,
            DateTime issuedAt)
        {
            var roleList = roles.Distinct(StringComparer.Ordinal).ToArray();
            var permissionList = permissions
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var accessToken = _accessTokenGenerator.Generate(
                new AccessTokenDescriptor(
                    user.Id,
                    user.UserName,
                    user.Email,
                    user.TokenVersion,
                    sessionId,
                    issuedAt,
                    roleList,
                    permissionList));

            return new AuthResponse
            {
                UserId = user.Id,
                Token = accessToken.Token,
                AccessToken = accessToken.Token,
                AccessTokenExpiresAt = accessToken.ExpiresAt,
                RefreshToken = refreshToken,
                RefreshTokenExpiresAt = refreshTokenExpiresAt,
                UserName = user.UserName,
                FullName = user.FullName,
                Email = user.Email,
                Roles = roleList,
                Permissions = permissionList
            };
        }

        public static string HashRefreshToken(string token)
            => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    public sealed record RefreshTokenIssue(
        string RawToken,
        RefreshToken Entity);
}
