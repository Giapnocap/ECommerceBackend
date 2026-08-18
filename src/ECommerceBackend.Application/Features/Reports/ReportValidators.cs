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

    public abstract class ReportDateRangeQueryValidator<TQuery>
        : AbstractValidator<TQuery>
        where TQuery : ReportDateRangeQuery
    {
        protected ReportDateRangeQueryValidator()
        {
            RuleFor(query => query.To)
                .GreaterThan(query => query.From)
                .When(query => query.From.HasValue && query.To.HasValue)
                .WithMessage("Thời điểm kết thúc phải lớn hơn thời điểm bắt đầu.");
        }
    }

    public sealed class RevenueReportQueryValidator
        : ReportDateRangeQueryValidator<RevenueReportQuery>
    {
        public RevenueReportQueryValidator()
        {
            RuleFor(query => query.GroupBy)
                .Must(groupBy => groupBy is not null
                    && new[] { "day", "week", "month" }.Contains(
                        groupBy.Trim(),
                        StringComparer.OrdinalIgnoreCase))
                .WithMessage("Kiểu nhóm doanh thu phải là day, week hoặc month.");
        }
    }

    public sealed class OrderReportQueryValidator
        : ReportDateRangeQueryValidator<OrderReportQuery>
    {
    }

    public sealed class ProductReportQueryValidator
        : ReportDateRangeQueryValidator<ProductReportQuery>
    {
        public ProductReportQueryValidator()
        {
            RuleFor(query => query.Limit)
                .InclusiveBetween(1, 100)
                .WithMessage("Số sản phẩm phải từ 1 đến 100.");
            RuleFor(query => query.LowStockThreshold)
                .InclusiveBetween(0, 1_000_000)
                .WithMessage("Ngưỡng tồn kho phải từ 0 đến 1.000.000.");
        }
    }

    public sealed class CustomerReportQueryValidator
        : ReportDateRangeQueryValidator<CustomerReportQuery>
    {
        public CustomerReportQueryValidator()
        {
            RuleFor(query => query.Limit)
                .InclusiveBetween(1, 100)
                .WithMessage("Số khách hàng phải từ 1 đến 100.");
        }
    }

    public sealed class ReturnReportQueryValidator
        : ReportDateRangeQueryValidator<ReturnReportQuery>
    {
        public ReturnReportQueryValidator()
        {
            RuleFor(query => query.ReasonLimit)
                .InclusiveBetween(1, 100)
                .WithMessage("Số lý do trả hàng phải từ 1 đến 100.");
        }
    }
}
