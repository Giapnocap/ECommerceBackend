namespace ECommerceBackend.Domain.Common
{
    public readonly record struct StatusChange<TStatus>(
        TStatus Previous,
        TStatus Current,
        bool Changed)
        where TStatus : struct, Enum;
}