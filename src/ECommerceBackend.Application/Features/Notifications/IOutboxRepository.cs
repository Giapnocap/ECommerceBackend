using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IOutboxRepository
    {
        Task<PageSlice<DeadLetterResponse>> GetDeadLettersAsync(
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        void Add(OutboxMessage message);
    }
}
