using ECommerceBackend.Application.DTOs;
using FluentValidation;

namespace ECommerceBackend.Application.Validation
{
    public class SalesSummaryQueryValidator : AbstractValidator<SalesSummaryQuery>
    {
        public SalesSummaryQueryValidator()
        {
            RuleFor(x => x.To)
                .GreaterThan(x => x.From)
                .WithMessage("Thời điểm kết thúc phải lớn hơn thời điểm bắt đầu.")
                .When(x => x.From.HasValue && x.To.HasValue);

            RuleFor(x => x.LowStockThreshold)
                .InclusiveBetween(0, 1_000_000)
                .WithMessage("Ngưỡng tồn kho phải từ 0 đến 1.000.000.");

            RuleFor(x => x.TopProductLimit)
                .InclusiveBetween(1, 100)
                .WithMessage("Số sản phẩm bán chạy phải từ 1 đến 100.");
        }
    }
}
