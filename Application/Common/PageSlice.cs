namespace ECommerceBackend.Application.Common
{
    public sealed record PageSlice<T>(
        IReadOnlyList<T> Items,
        int TotalCount);
}
