namespace ECommerceBackend.Domain.Common
{
    public sealed class DomainRuleViolationException : Exception
    {
        public string Code { get; }

        public DomainRuleViolationException(string code, string message)
            : base(message)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Domain rule code is required.", nameof(code));

            Code = code;
        }
    }
}
