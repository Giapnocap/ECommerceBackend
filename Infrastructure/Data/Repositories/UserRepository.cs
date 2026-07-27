using ECommerceBackend.Application.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class UserRepository : IUserRepository
    {
        private readonly AppDbContext _context;

        public UserRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<string?> GetActiveEmailAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => _context.Users
                .AsNoTracking()
                .Where(user => user.Id == userId && !user.IsDeleted)
                .Select(user => user.Email)
                .SingleOrDefaultAsync(cancellationToken);
    }
}
