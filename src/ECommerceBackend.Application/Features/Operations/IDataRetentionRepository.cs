using ECommerceBackend.Application.Common;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IDataRetentionRepository
    {
        Task<DataRetentionBatch> LoadCandidatesAsync(
            int batchSize,
            DateTime processedOutboxCutoff,
            DateTime expiredRefreshTokenCutoff,
            DateTime webhookPayloadCutoff,
            CancellationToken cancellationToken = default);

        void Apply(DataRetentionBatch batch);
    }
}
