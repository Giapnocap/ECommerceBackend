namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IUserRepository
    {
        Task<string?> GetActiveEmailAsync(
            Guid userId,
            CancellationToken cancellationToken = default);
    }
}
