namespace ECommerceBackend.Infrastructure.Maintenance
{
    public sealed class DataRetentionWorkerStatus
    {
        private long _startedAtTicks;
        private long _lastSuccessfulCycleAtTicks;
        private long _lastFailureAtTicks;
        private long _lastChangedRecordCount;

        public void MarkStarted(DateTime occurredAt)
            => Interlocked.Exchange(ref _startedAtTicks, ToUtcTicks(occurredAt));

        public void MarkSuccessfulCycle(DateTime occurredAt, int changedRecordCount)
        {
            Interlocked.Exchange(ref _lastChangedRecordCount, changedRecordCount);
            Interlocked.Exchange(ref _lastSuccessfulCycleAtTicks, ToUtcTicks(occurredAt));
        }

        public void MarkFailure(DateTime occurredAt)
            => Interlocked.Exchange(ref _lastFailureAtTicks, ToUtcTicks(occurredAt));

        public DataRetentionWorkerSnapshot GetSnapshot()
            => new(
                FromUtcTicks(Interlocked.Read(ref _startedAtTicks)),
                FromUtcTicks(Interlocked.Read(ref _lastSuccessfulCycleAtTicks)),
                FromUtcTicks(Interlocked.Read(ref _lastFailureAtTicks)),
                Interlocked.Read(ref _lastChangedRecordCount));

        private static long ToUtcTicks(DateTime value)
            => value.ToUniversalTime().Ticks;

        private static DateTime? FromUtcTicks(long ticks)
            => ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
    }

    public sealed record DataRetentionWorkerSnapshot(
        DateTime? StartedAt,
        DateTime? LastSuccessfulCycleAt,
        DateTime? LastFailureAt,
        long LastChangedRecordCount);
}
