using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Infrastructure.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class DataRetentionHostedServiceTests
{
    [Fact]
    public async Task ProcessCycle_UsesSystemActorAndStopsAfterPartialBatch()
    {
        var operations = new RecordingOperationsService(
        [
            Response(processedOutboxCandidates: 1, processedOutboxDeleted: 1),
            Response(expiredTokenCandidates: 1, expiredTokenDeleted: 1),
            Response()
        ]);
        var services = new ServiceCollection();
        services.AddScoped<IOperationsService>(_ => operations);
        await using var provider = services.BuildServiceProvider();
        var worker = new DataRetentionHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DataRetentionOptions
            {
                Enabled = true,
                AutomaticProcessingEnabled = true,
                MaxBatchSize = 1,
                MaxBatchesPerCycle = 10
            }),
            TimeProvider.System,
            NullLogger<DataRetentionHostedService>.Instance,
            new DataRetentionWorkerStatus());

        var changedRecordCount = await worker.ProcessCycleAsync();

        Assert.Equal(2, changedRecordCount);
        Assert.Equal(3, operations.Requests.Count);
        Assert.All(operations.Requests, request => Assert.True(request.ApplyChanges));
        Assert.All(operations.ActorUserIds, actorUserId => Assert.Null(actorUserId));
    }

    [Fact]
    public async Task ProcessCycle_DoesNotExceedConfiguredBatchLimit()
    {
        var operations = new RecordingOperationsService(
        [
            Response(processedOutboxCandidates: 1, processedOutboxDeleted: 1),
            Response(processedOutboxCandidates: 1, processedOutboxDeleted: 1),
            Response(processedOutboxCandidates: 1, processedOutboxDeleted: 1)
        ]);
        var services = new ServiceCollection();
        services.AddScoped<IOperationsService>(_ => operations);
        await using var provider = services.BuildServiceProvider();
        var worker = new DataRetentionHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DataRetentionOptions
            {
                Enabled = true,
                AutomaticProcessingEnabled = true,
                MaxBatchSize = 1,
                MaxBatchesPerCycle = 2
            }),
            TimeProvider.System,
            NullLogger<DataRetentionHostedService>.Instance,
            new DataRetentionWorkerStatus());

        var changedRecordCount = await worker.ProcessCycleAsync();

        Assert.Equal(2, changedRecordCount);
        Assert.Equal(2, operations.Requests.Count);
    }

    private static DataRetentionResponse Response(
        int processedOutboxCandidates = 0,
        int processedOutboxDeleted = 0,
        int expiredTokenCandidates = 0,
        int expiredTokenDeleted = 0)
        => new()
        {
            ProcessedOutboxCandidateCount = processedOutboxCandidates,
            ProcessedOutboxDeletedCount = processedOutboxDeleted,
            ExpiredRefreshTokenCandidateCount = expiredTokenCandidates,
            ExpiredRefreshTokenDeletedCount = expiredTokenDeleted
        };

    private sealed class RecordingOperationsService : IOperationsService
    {
        private readonly Queue<DataRetentionResponse> _responses;

        public RecordingOperationsService(IEnumerable<DataRetentionResponse> responses)
        {
            _responses = new Queue<DataRetentionResponse>(responses);
        }

        public List<DataRetentionRequest> Requests { get; } = [];
        public List<Guid?> ActorUserIds { get; } = [];

        public Task<DataRetentionResponse> RunDataRetentionAsync(
            DataRetentionRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            ActorUserIds.Add(actorUserId);
            return Task.FromResult(_responses.Dequeue());
        }

        public Task<PagedResult<DeadLetterResponse>> GetDeadLettersAsync(
            DeadLetterQueryParams query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RedriveOutboxResponse> RedriveDeadLetterAsync(
            Guid messageId,
            Guid actorUserId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PagedResult<AuditEventResponse>> GetAuditEventsAsync(
            AuditQueryParams query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
