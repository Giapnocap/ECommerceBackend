using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace ECommerceBackend.Infrastructure.Security
{
    public sealed class DataProtectionSensitivePayloadProtector
        : ISensitivePayloadProtector
    {
        private const string Purpose = "ECommerceBackend.Outbox.SensitivePayload.v1";
        private readonly IDataProtector _protector;

        public DataProtectionSensitivePayloadProtector(
            IDataProtectionProvider dataProtectionProvider)
        {
            _protector = dataProtectionProvider.CreateProtector(Purpose);
        }

        public string Protect(string plaintext) => _protector.Protect(plaintext);

        public string Unprotect(string protectedPayload)
            => _protector.Unprotect(protectedPayload);
    }
}
