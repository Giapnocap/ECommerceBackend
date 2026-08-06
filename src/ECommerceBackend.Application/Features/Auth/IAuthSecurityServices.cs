namespace ECommerceBackend.Application.Interfaces
{
    public interface IAccessTokenGenerator
    {
        AccessTokenIssue Generate(AccessTokenDescriptor descriptor);
    }

    public sealed record AccessTokenDescriptor(
        Guid UserId,
        string UserName,
        string Email,
        int TokenVersion,
        Guid SessionId,
        DateTime IssuedAt,
        IReadOnlyCollection<string> Roles,
        IReadOnlyCollection<string> Permissions);

    public sealed record AccessTokenIssue(
        string Token,
        DateTime ExpiresAt);

    public interface IPasswordHasher
    {
        string Hash(string password);
        bool Verify(string password, string? passwordHash);
    }

    public interface ISensitivePayloadProtector
    {
        string Protect(string plaintext);
        string Unprotect(string protectedPayload);
    }
}
