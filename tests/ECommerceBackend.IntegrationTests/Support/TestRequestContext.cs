using ECommerceBackend.Application.Interfaces;

namespace ECommerceBackend.Tests.Support;

internal sealed record TestRequestContext(
    Guid? ActorUserId = null,
    string CorrelationId = "background",
    string? IpAddress = null) : IRequestContext;
