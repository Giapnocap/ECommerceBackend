namespace ECommerceBackend.Application.Common
{
    public sealed record StatusSummary<TStatus>(
        TStatus Status,
        int Count,
        decimal Amount)
        where TStatus : struct, Enum;
}
