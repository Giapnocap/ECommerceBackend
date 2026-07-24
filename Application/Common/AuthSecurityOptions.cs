namespace ECommerceBackend.Application.Common
{
    public sealed class AuthSecurityOptions
    {
        public const string SectionName = "AuthSecurity";

        public int MaxFailedLoginAttempts { get; set; } = 5;
        public int LockoutMinutes { get; set; } = 15;
        public int PasswordResetTokenMinutes { get; set; } = 30;
        public string PasswordResetUrl { get; set; } =
            "http://localhost:3000/reset-password";
    }
}
