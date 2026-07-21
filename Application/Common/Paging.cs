namespace ECommerceBackend.Application.Common
{
    public static class Paging
    {
        public const int DefaultPage = 1;
        public const int DefaultPageSize = 12;
        public const int MaxPageSize = 100;

        public static PagingQuery Normalize(int page, int size, int defaultSize = DefaultPageSize)
        {
            var normalizedPage = page <= 0 ? DefaultPage : Math.Min(page, CommerceLimits.MaxPage);
            var fallbackSize = defaultSize <= 0 ? DefaultPageSize : defaultSize;
            var normalizedSize = size <= 0 ? fallbackSize : Math.Min(size, MaxPageSize);
            return new PagingQuery(normalizedPage, normalizedSize);
        }

        public static int GetSkipCount(PagingQuery paging)
            => (paging.Page - 1) * paging.Size;
    }

    public readonly record struct PagingQuery(int Page, int Size);
}
