using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Tests;

public class PaymentRulesTests
{
    private static readonly HashSet<(PaymentStatus Current, PaymentStatus Next)>
        AllowedTransitions =
        [
            (PaymentStatus.Pending, PaymentStatus.Paid),
            (PaymentStatus.Pending, PaymentStatus.Failed),
            (PaymentStatus.Pending, PaymentStatus.Cancelled),
            (PaymentStatus.Paid, PaymentStatus.Refunded)
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
