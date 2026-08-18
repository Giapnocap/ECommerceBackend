using ECommerceBackend.Application.DTOs;
using FluentValidation;

namespace ECommerceBackend.Application.Validation
{
    public sealed class DashboardSummaryQueryValidator : AbstractValidator<DashboardSummaryQuery>
    {
        public DashboardSummaryQueryValidator()
        {
            RuleFor(query => query.LowStockThreshold)
                .InclusiveBetween(0, 1_000_000)
                .WithMessage("Ngưỡng tồn kho phải từ 0 đến 1.000.000.");
        }
    }

    public sealed class DashboardRevenueQueryValidator : AbstractValidator<DashboardRevenueQuery>
    {
        public DashboardRevenueQueryValidator()
        {
            RuleFor(query => query.From)
                .LessThan(query => query.To)
                .When(query => query.From.HasValue && query.To.HasValue)
                .WithMessage("Thời điểm bắt đầu phải nhỏ hơn thời điểm kết thúc.");
            RuleFor(query => query.GroupBy)
                .NotEmpty()
                .Must(groupBy => groupBy is not null
                    && (groupBy.Equals("day", StringComparison.OrdinalIgnoreCase)
                        || groupBy.Equals("week", StringComparison.OrdinalIgnoreCase)
                        || groupBy.Equals("month", StringComparison.OrdinalIgnoreCase)))
                .WithMessage("Kiểu nhóm doanh thu phải là day, week hoặc month.");
        }
    }

    public sealed class DashboardTopProductsQueryValidator : AbstractValidator<DashboardTopProductsQuery>
    {
        public DashboardTopProductsQueryValidator()
        {
            RuleFor(query => query.From)
                .LessThan(query => query.To)
                .When(query => query.From.HasValue && query.To.HasValue)
                .WithMessage("Thời điểm bắt đầu phải nhỏ hơn thời điểm kết thúc.");
            RuleFor(query => query.Limit)
                .InclusiveBetween(1, 100)
                .WithMessage("Số sản phẩm bán chạy phải từ 1 đến 100.");
        }
    }

    public sealed class DashboardRecentActivitiesQueryValidator
        : AbstractValidator<DashboardRecentActivitiesQuery>
    {
        public DashboardRecentActivitiesQueryValidator()
        {
            RuleFor(query => query.Limit)
                .InclusiveBetween(1, 10)
                .WithMessage("Số hoạt động gần đây phải từ 1 đến 10.");
        }
    }
}
