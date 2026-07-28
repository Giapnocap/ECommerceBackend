using System.Security.Cryptography;
using ECommerceBackend.Application.Interfaces;

namespace ECommerceBackend.Infrastructure.Security
{
    public sealed class BCryptPasswordHasher : IPasswordHasher
    {
        private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword(
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

        public string Hash(string password)
            => BCrypt.Net.BCrypt.HashPassword(password);

        public bool Verify(string password, string? passwordHash)
        {
            var hasUsableHash = passwordHash?.StartsWith("$2", StringComparison.Ordinal) == true;
            var hashToVerify = hasUsableHash ? passwordHash! : DummyHash;

            try
            {
                var verified = BCrypt.Net.BCrypt.Verify(password, hashToVerify);
                return hasUsableHash && verified;
            }
            catch (BCrypt.Net.SaltParseException) when (hasUsableHash)
            {
                _ = BCrypt.Net.BCrypt.Verify(password, DummyHash);
                return false;
            }
        }
    }
}
