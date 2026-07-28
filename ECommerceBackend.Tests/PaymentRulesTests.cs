using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Tests;

public class PaymentRulesTests
{
    [Theory]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Paid, true)]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Failed, true)]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Cancelled, true)]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Refunded, false)]
    [InlineData(PaymentStatus.Paid, PaymentStatus.Refunded, true)]
    [InlineData(PaymentStatus.Paid, PaymentStatus.Failed, false)]
    [InlineData(PaymentStatus.Paid, PaymentStatus.Cancelled, false)]
    [InlineData(PaymentStatus.Failed, PaymentStatus.Paid, false)]
    [InlineData(PaymentStatus.Cancelled, PaymentStatus.Paid, false)]
    [InlineData(PaymentStatus.Refunded, PaymentStatus.Paid, false)]
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
    public void PaymentStatusTransition_SameStatus_IsIdempotent(PaymentStatus status)
    {
        Assert.True(status.CanTransitionTo(status));
    }

    [Fact]
    public void PaymentLifecycle_InitialStatus_IsPending()
    {
        Assert.Equal(PaymentStatus.Pending, PaymentStatusTransitions.Initial);
    }
}
