using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Tests;

public class PaymentRulesTests
{
    private static readonly HashSet<(PaymentStatus Current, PaymentStatus Next)>
        AllowedTransitions =
        [
            (PaymentStatus.Pending, PaymentStatus.Paid),
            (PaymentStatus.Pending, PaymentStatus.RequiresAction),
            (PaymentStatus.Pending, PaymentStatus.Processing),
            (PaymentStatus.Pending, PaymentStatus.Failed),
            (PaymentStatus.Pending, PaymentStatus.Cancelled),
            (PaymentStatus.RequiresAction, PaymentStatus.Pending),
            (PaymentStatus.RequiresAction, PaymentStatus.Processing),
            (PaymentStatus.RequiresAction, PaymentStatus.Paid),
            (PaymentStatus.RequiresAction, PaymentStatus.Failed),
            (PaymentStatus.RequiresAction, PaymentStatus.Cancelled),
            (PaymentStatus.Processing, PaymentStatus.Pending),
            (PaymentStatus.Processing, PaymentStatus.RequiresAction),
            (PaymentStatus.Processing, PaymentStatus.Paid),
            (PaymentStatus.Processing, PaymentStatus.Failed),
            (PaymentStatus.Processing, PaymentStatus.Cancelled),
            (PaymentStatus.Paid, PaymentStatus.PartiallyRefunded),
            (PaymentStatus.Paid, PaymentStatus.Refunded),
            (PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded)
        ];

    public static IEnumerable<object[]> TransitionCases()
    {
        foreach (var current in Enum.GetValues<PaymentStatus>())
        {
            foreach (var next in Enum.GetValues<PaymentStatus>())
            {
                yield return
                [
                    current,
                    next,
                    current == next
                        || AllowedTransitions.Contains((current, next))
                ];
            }
        }
    }

    [Theory]
    [MemberData(nameof(TransitionCases))]
    public void PaymentStatusTransition_FollowsBusinessStateMachine(
        PaymentStatus current,
        PaymentStatus next,
        bool expected)
    {
        Assert.Equal(expected, current.CanTransitionTo(next));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Paid)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.RequiresAction)]
    [InlineData(PaymentStatus.Processing)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    public void PaymentStatusTransition_SameStatus_IsIdempotent(PaymentStatus status)
    {
        Assert.True(status.CanTransitionTo(status));
    }

    [Fact]
    public void PaymentLifecycle_InitialStatus_IsPending()
    {
        Assert.Equal(PaymentStatus.Pending, PaymentStatusTransitions.Initial);
    }

    [Fact]
    public void PaymentCreation_NormalizesCurrencyWithoutChangingLegacyDefaults()
    {
        var payment = Payment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentMethod.Card,
            100_000,
            "stripe",
            null,
            DateTime.UtcNow,
            "vnd");

        Assert.Equal("VND", payment.Currency);
        Assert.Equal(0, payment.RefundedAmount);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public void PaymentRefund_ProtectsPaidAmountAndSupportsPartialThenFull()
    {
        var createdAt = DateTime.UtcNow;
        var payment = Payment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentMethod.Card,
            100_000,
            "stripe",
            "pi_test",
            createdAt);
        payment.ChangeStatus(PaymentStatus.Paid, createdAt.AddMinutes(1));

        payment.RecordRefund(25_000, createdAt.AddMinutes(2));
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(25_000, payment.RefundedAmount);

        payment.RecordRefund(75_000, createdAt.AddMinutes(3));
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(payment.Amount, payment.RefundedAmount);
    }

    [Fact]
    public void Reconciliation_RecordsObservationEvenWhenStatusIsUnchanged()
    {
        var createdAt = DateTime.UtcNow.AddMinutes(-10);
        var payment = Payment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentMethod.Card,
            100_000,
            "stripe",
            "pi_test",
            createdAt);
        payment.ChangeStatus(
            PaymentStatus.Processing,
            createdAt.AddMinutes(1));
        var observedAt = createdAt.AddMinutes(5);

        var change = payment.ReconcileProviderStatus(
            PaymentStatus.Processing,
            observedAt);

        Assert.False(change.Changed);
        Assert.Equal(observedAt, payment.LastReconciledAt);
    }
}
