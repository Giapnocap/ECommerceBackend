namespace ECommerceBackend.Application.Interfaces
{
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
