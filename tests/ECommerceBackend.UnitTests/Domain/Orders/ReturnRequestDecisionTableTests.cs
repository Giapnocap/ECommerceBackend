using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Tests;

public sealed class ReturnRequestDecisionTableTests
{
    private static readonly DateTime RequestedAt = new(
        2026,
        7,
        29,
        10,
        0,
        0,
        DateTimeKind.Utc);

    public static IEnumerable<object[]> StatusCases()
        => Enum.GetValues<ReturnRequestStatus>()
            .Select(status => new object[] { status });

    [Theory]
    [MemberData(nameof(StatusCases))]
    public void Review_IsAllowedOnlyWhilePending(
        ReturnRequestStatus currentStatus)
    {
        var request = CreateInStatus(currentStatus);

        if (currentStatus == ReturnRequestStatus.Pending)
        {
            request.Review(
                ReturnReviewDecision.Approve,
                Guid.NewGuid(),
                RequestedAt.AddMinutes(1),
                "Approved");

            Assert.Equal(ReturnRequestStatus.Approved, request.Status);
            return;
        }

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            request.Review(
                ReturnReviewDecision.Approve,
                Guid.NewGuid(),
                RequestedAt.AddMinutes(5),
                "Approved again"));

        Assert.Equal("return_request_already_reviewed", exception.Code);
        Assert.Equal(currentStatus, request.Status);
    }

    [Theory]
    [MemberData(nameof(StatusCases))]
    public void Receive_IsAllowedOnlyAfterApproval(
        ReturnRequestStatus currentStatus)
    {
        var request = CreateInStatus(currentStatus);

        if (currentStatus == ReturnRequestStatus.Approved)
        {
            request.Receive(
                Guid.NewGuid(),
                RequestedAt.AddMinutes(2),
                "Item inspected");

            Assert.Equal(ReturnRequestStatus.Received, request.Status);
            return;
        }

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            request.Receive(
                Guid.NewGuid(),
                RequestedAt.AddMinutes(5),
                "Item inspected"));

        Assert.Equal("return_receive_requires_approved", exception.Code);
        Assert.Equal(currentStatus, request.Status);
    }

    [Theory]
    [MemberData(nameof(StatusCases))]
    public void Refund_IsAllowedOnlyAfterReceipt(
        ReturnRequestStatus currentStatus)
    {
        var request = CreateInStatus(currentStatus);

        if (currentStatus == ReturnRequestStatus.Received)
        {
            request.MarkRefunded(RequestedAt.AddMinutes(3));

            Assert.Equal(ReturnRequestStatus.Refunded, request.Status);
            return;
        }

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            request.MarkRefunded(RequestedAt.AddMinutes(5)));

        Assert.Equal("return_refund_requires_received", exception.Code);
        Assert.Equal(currentStatus, request.Status);
    }

    [Fact]
    public void Lifecycle_RejectsOutOfOrderTimestamps()
    {
        var pending = CreateInStatus(ReturnRequestStatus.Pending);
        var reviewException =
            Assert.Throws<DomainRuleViolationException>(() =>
                pending.Review(
                    ReturnReviewDecision.Approve,
                    Guid.NewGuid(),
                    RequestedAt.AddTicks(-1),
                    "Approved"));
        Assert.Equal("return_review_invalid", reviewException.Code);

        var approved = CreateInStatus(ReturnRequestStatus.Approved);
        var receiveException =
            Assert.Throws<DomainRuleViolationException>(() =>
                approved.Receive(
                    Guid.NewGuid(),
                    RequestedAt,
                    "Item inspected"));
        Assert.Equal("return_receive_invalid", receiveException.Code);

        var received = CreateInStatus(ReturnRequestStatus.Received);
        var refundException =
            Assert.Throws<DomainRuleViolationException>(() =>
                received.MarkRefunded(
                    RequestedAt.AddMinutes(1)));
        Assert.Equal("return_refund_time_invalid", refundException.Code);
    }

    private static ReturnRequest CreateInStatus(
        ReturnRequestStatus status)
    {
        var request = ReturnRequest.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Product does not meet expectations",
            RequestedAt);

        if (status == ReturnRequestStatus.Pending)
            return request;

        request.Review(
            status == ReturnRequestStatus.Rejected
                ? ReturnReviewDecision.Reject
                : ReturnReviewDecision.Approve,
            Guid.NewGuid(),
            RequestedAt.AddMinutes(1),
            status == ReturnRequestStatus.Rejected
                ? "Rejected by policy"
                : "Approved");

        if (status is ReturnRequestStatus.Approved
            or ReturnRequestStatus.Rejected)
        {
            return request;
        }

        request.Receive(
            Guid.NewGuid(),
            RequestedAt.AddMinutes(2),
            "Item inspected");
        if (status == ReturnRequestStatus.Received)
            return request;

        request.MarkRefunded(RequestedAt.AddMinutes(3));
        return request;
    }
}
