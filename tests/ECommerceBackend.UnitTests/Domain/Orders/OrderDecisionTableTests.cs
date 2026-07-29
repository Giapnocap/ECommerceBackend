using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Tests;

public sealed class OrderDecisionTableTests
{
    private static readonly HashSet<(OrderStatus Current, OrderStatus Next)>
        AllowedTransitions =
        [
            (OrderStatus.Pending, OrderStatus.Confirmed),
            (OrderStatus.Pending, OrderStatus.Cancelled),
            (OrderStatus.Confirmed, OrderStatus.Shipping),
            (OrderStatus.Confirmed, OrderStatus.Cancelled),
            (OrderStatus.Shipping, OrderStatus.Delivered),
            (OrderStatus.Shipping, OrderStatus.DeliveryFailed),
            (OrderStatus.DeliveryFailed, OrderStatus.Shipping),
            (OrderStatus.DeliveryFailed, OrderStatus.Cancelled),
            (OrderStatus.Delivered, OrderStatus.ReturnRequested),
            (OrderStatus.ReturnRequested, OrderStatus.ReturnApproved),
            (OrderStatus.ReturnRequested, OrderStatus.Delivered),
            (OrderStatus.ReturnApproved, OrderStatus.Returned),
            (OrderStatus.Returned, OrderStatus.Refunded)
        ];

    public static IEnumerable<object[]> TransitionCases()
    {
        foreach (var current in Enum.GetValues<OrderStatus>())
        {
            foreach (var next in Enum.GetValues<OrderStatus>())
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

    public static IEnumerable<object?[]> CancellationPaymentCases()
    {
        yield return [null, null];
        yield return [PaymentStatus.Pending, null];
        yield return [PaymentStatus.Failed, null];
        yield return [PaymentStatus.Cancelled, null];
        yield return
        [
            PaymentStatus.Paid,
            "order_paid_cancellation_forbidden"
        ];
        yield return
        [
            PaymentStatus.Refunded,
            "order_refunded_cancellation_forbidden"
        ];
    }

    [Theory]
    [MemberData(nameof(TransitionCases))]
    public void OrderStatusTransition_FollowsCompleteDecisionTable(
        OrderStatus current,
        OrderStatus next,
        bool expected)
    {
        Assert.Equal(expected, current.CanTransitionTo(next));
    }

    [Theory]
    [MemberData(nameof(CancellationPaymentCases))]
    public void Cancellation_UsesPaymentDecisionTable(
        PaymentStatus? paymentStatus,
        string? expectedErrorCode)
    {
        var occurredAt = new DateTime(
            2026,
            7,
            29,
            10,
            0,
            0,
            DateTimeKind.Utc);
        var order = new Order { OrderDate = occurredAt.AddMinutes(-1) };

        if (expectedErrorCode == null)
        {
            var change = order.Cancel(
                occurredAt,
                paymentStatus,
                "Customer requested cancellation");

            Assert.True(change.Changed);
            Assert.Equal(OrderStatus.Cancelled, order.Status);
            return;
        }

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            order.Cancel(
                occurredAt,
                paymentStatus,
                "Customer requested cancellation"));

        Assert.Equal(expectedErrorCode, exception.Code);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }
}
