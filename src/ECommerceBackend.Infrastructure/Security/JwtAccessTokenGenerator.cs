using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ECommerceBackend.Infrastructure.Security
{
    public sealed class JwtAccessTokenGenerator : IAccessTokenGenerator
    {
        private readonly JwtOptions _options;

        public JwtAccessTokenGenerator(IOptions<JwtOptions> options)
        {
            _options = options.Value;
        }

        public AccessTokenIssue Generate(AccessTokenDescriptor descriptor)
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_options.Key));
            var credentials = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);
            var expiresAt = descriptor.IssuedAt.AddMinutes(_options.AccessTokenMinutes);
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, descriptor.UserId.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.NameIdentifier, descriptor.UserId.ToString()),
                new(ClaimTypes.Name, descriptor.UserName),
                new(ClaimTypes.Email, descriptor.Email),
                new(
                    AuthClaimTypes.TokenVersion,
                    descriptor.TokenVersion.ToString()),
                new(AuthClaimTypes.SessionId, descriptor.SessionId.ToString())
            };
            claims.AddRange(
                descriptor.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
            claims.AddRange(
                descriptor.Permissions.Select(permission =>
                    new Claim(AuthClaimTypes.Permission, permission)));

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                expires: expiresAt,
                claims: claims,
                signingCredentials: credentials);
            return new AccessTokenIssue(
                new JwtSecurityTokenHandler().WriteToken(token),
                expiresAt);
        }
    }
}
