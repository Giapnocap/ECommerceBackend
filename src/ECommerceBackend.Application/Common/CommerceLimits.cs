namespace ECommerceBackend.Application.Common
{
    public static class CommerceLimits
    {
        public const int MoneyPrecision = 18;
        public const int MoneyScale = 2;
        public const decimal MaxMoneyAmount = 9999999999999999.99m;
        public const int MaxPage = int.MaxValue / Paging.MaxPageSize;
    }
}
