namespace ECommerceBackend.Domain.Common
{
    public sealed record CurrencyMetadata(
        string Code,
        int DecimalPlaces,
        MidpointRounding RoundingMode)
    {
        public decimal Round(decimal amount)
            => decimal.Round(amount, DecimalPlaces, RoundingMode);

        public long ToMinorUnits(decimal amount)
        {
            var rounded = Round(amount);
            if (rounded != amount)
            {
                throw new DomainRuleViolationException(
                    "money_scale_invalid",
                    $"Số tiền {Code} có quá nhiều chữ số thập phân.");
            }

            var multiplier = Pow10(DecimalPlaces);
            try
            {
                return decimal.ToInt64(checked(amount * multiplier));
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "money_minor_units_overflow",
                    $"Số tiền {Code} vượt quá giới hạn xử lý.");
            }
        }

        public decimal FromMinorUnits(long amount)
            => amount / Pow10(DecimalPlaces);

        private static decimal Pow10(int exponent)
        {
            decimal result = 1;
            for (var index = 0; index < exponent; index++)
                result *= 10;
            return result;
        }
    }

    public static class CurrencyCatalog
    {
        public const string BaseCurrency = "VND";

        private static readonly IReadOnlyDictionary<string, CurrencyMetadata>
            Metadata = new Dictionary<string, CurrencyMetadata>(
                StringComparer.Ordinal)
            {
                ["VND"] = new("VND", 0, MidpointRounding.AwayFromZero),
                ["USD"] = new("USD", 2, MidpointRounding.AwayFromZero),
                ["EUR"] = new("EUR", 2, MidpointRounding.AwayFromZero)
            };

        public static IReadOnlyCollection<string> SupportedCodes
            => Metadata.Keys.ToArray();

        public static bool IsSupported(string? currency)
            => currency != null
                && Metadata.ContainsKey(currency.Trim().ToUpperInvariant());

        public static CurrencyMetadata Get(string currency)
        {
            var normalized = Normalize(currency);
            return Metadata.TryGetValue(normalized, out var metadata)
                ? metadata
                : throw new DomainRuleViolationException(
                    "currency_not_supported",
                    $"Tiền tệ '{normalized}' chưa được hỗ trợ.");
        }

        public static string Normalize(string currency)
        {
            var normalized = currency?.Trim().ToUpperInvariant();
            if (normalized?.Length != 3
                || normalized.Any(character => character is < 'A' or > 'Z'))
            {
                throw new DomainRuleViolationException(
                    "currency_invalid",
                    "Mã tiền tệ phải gồm 3 chữ cái theo chuẩn ISO.");
            }

            return normalized;
        }
    }

    public readonly record struct Money
    {
        public const decimal MaxAmount = 9999999999999999.99m;

        public Money(decimal amount, string currency)
        {
            var metadata = CurrencyCatalog.Get(currency);
            if (amount < 0
                || amount > MaxAmount
                || metadata.Round(amount) != amount)
            {
                throw new DomainRuleViolationException(
                    "money_invalid",
                    $"Số tiền {metadata.Code} không hợp lệ.");
            }

            Amount = amount;
            Currency = metadata.Code;
        }

        public decimal Amount { get; }
        public string Currency { get; }

        public static Money Round(decimal amount, string currency)
        {
            var metadata = CurrencyCatalog.Get(currency);
            return new Money(metadata.Round(amount), metadata.Code);
        }

        public Money Convert(decimal exchangeRate, string targetCurrency)
        {
            if (exchangeRate <= 0)
            {
                throw new DomainRuleViolationException(
                    "exchange_rate_invalid",
                    "Tỷ giá phải lớn hơn 0.");
            }

            try
            {
                return Round(
                    checked(Amount * exchangeRate),
                    targetCurrency);
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "money_conversion_overflow",
                    "Số tiền sau quy đổi vượt quá giới hạn xử lý.");
            }
        }
    }
}
