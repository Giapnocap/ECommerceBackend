namespace ECommerceBackend.Application.Interfaces
{
    public interface IRequestContext
    {
        Guid? ActorUserId { get; }
        string CorrelationId { get; }
        string? IpAddress { get; }
    }
}
