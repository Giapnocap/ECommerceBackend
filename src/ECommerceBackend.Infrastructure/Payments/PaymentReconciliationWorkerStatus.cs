namespace ECommerceBackend.Infrastructure.Payments
{
    public sealed class PaymentReconciliationWorkerStatus
    {
        private long _startedAtTicks;
        private long _lastSuccessfulCycleAtTicks;
        private long _lastFailureAtTicks;

        public void MarkStarted(DateTime occurredAt)
            => Interlocked.Exchange(
                ref _startedAtTicks,
                ToUtcTicks(occurredAt));

        public void MarkSuccessfulCycle(DateTime occurredAt)
            => Interlocked.Exchange(
                ref _lastSuccessfulCycleAtTicks,
                ToUtcTicks(occurredAt));

        public void MarkFailure(DateTime occurredAt)
            => Interlocked.Exchange(
                ref _lastFailureAtTicks,
                ToUtcTicks(occurredAt));

        public PaymentReconciliationWorkerSnapshot GetSnapshot()
            => new(
                FromUtcTicks(Interlocked.Read(ref _startedAtTicks)),
                FromUtcTicks(Interlocked.Read(
                    ref _lastSuccessfulCycleAtTicks)),
                FromUtcTicks(Interlocked.Read(ref _lastFailureAtTicks)));

        private static long ToUtcTicks(DateTime value)
            => value.ToUniversalTime().Ticks;

        private static DateTime? FromUtcTicks(long ticks)
            => ticks == 0
                ? null
                : new DateTime(ticks, DateTimeKind.Utc);
    }

    public sealed record PaymentReconciliationWorkerSnapshot(
        DateTime? StartedAt,
        DateTime? LastSuccessfulCycleAt,
        DateTime? LastFailureAt);
}
