using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuthTokenIssuer
    {
        private readonly JwtOptions _options;

        public AuthTokenIssuer(IOptions<JwtOptions> options)
        {
            _options = options.Value;
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
                new RefreshToken
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    FamilyId = familyId,
                    TokenHash = HashRefreshToken(rawToken),
                    CreatedAt = occurredAt,
                    ExpiresAt = expiresAt
                });
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
            var accessToken = GenerateJwt(
                user,
                roleList,
                permissionList,
                sessionId,
                issuedAt,
                out var accessTokenExpiresAt);

            return new AuthResponse
            {
                UserId = user.Id,
                Token = accessToken,
                AccessToken = accessToken,
                AccessTokenExpiresAt = accessTokenExpiresAt,
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

        private string GenerateJwt(
            User user,
            IEnumerable<string> roles,
            IEnumerable<string> permissions,
            Guid sessionId,
            DateTime issuedAt,
            out DateTime expiresAt)
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_options.Key));
            var credentials = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);
            expiresAt = issuedAt.AddMinutes(_options.AccessTokenMinutes);
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.UserName),
                new(ClaimTypes.Email, user.Email),
                new(
                    AuthClaimTypes.TokenVersion,
                    user.TokenVersion.ToString()),
                new(AuthClaimTypes.SessionId, sessionId.ToString())
            };
            claims.AddRange(
                roles.Select(role => new Claim(ClaimTypes.Role, role)));
            claims.AddRange(
                permissions.Select(permission =>
                    new Claim(AuthClaimTypes.Permission, permission)));

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                expires: expiresAt,
                claims: claims,
                signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    public sealed record RefreshTokenIssue(
        string RawToken,
        RefreshToken Entity);
}
