using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using FluentValidation;

namespace ECommerceBackend.Application.Validation
{
    public sealed class CustomerQueryParamsValidator : AbstractValidator<CustomerQueryParams>
    {
        private static readonly string[] AllowedStatuses = ["active", "locked"];
        private static readonly string[] AllowedSortFields = ["name", "registeredat", "orders", "spent"];
        private static readonly string[] AllowedSortOrders = ["asc", "desc"];

        public CustomerQueryParamsValidator()
        {
            RuleFor(query => query.Keyword)
                .MaximumLength(100)
                .When(query => query.Keyword != null)
                .WithMessage("Từ khóa không được vượt quá 100 ký tự.");
            RuleFor(query => query.Status)
                .Must(status => status == null
                    || AllowedStatuses.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase))
                .WithMessage("Trạng thái khách hàng phải là active hoặc locked.");
            RuleFor(query => query.RegisteredTo)
                .GreaterThan(query => query.RegisteredFrom)
                .When(query => query.RegisteredFrom.HasValue && query.RegisteredTo.HasValue)
                .WithMessage("Thời điểm kết thúc phải lớn hơn thời điểm bắt đầu.");
            RuleFor(query => query.SortBy)
                .Must(sortBy => sortBy == null
                    || AllowedSortFields.Contains(sortBy.ToLowerInvariant()))
                .WithMessage("Tiêu chí sắp xếp chỉ chấp nhận: tên, ngày đăng ký, số đơn hoặc chi tiêu.");
            RuleFor(query => query.SortOrder)
                .Must(sortOrder => sortOrder == null
                    || AllowedSortOrders.Contains(sortOrder.ToLowerInvariant()))
                .WithMessage("Thứ tự sắp xếp chỉ chấp nhận tăng dần hoặc giảm dần.");
            RuleFor(query => query.Page)
                .InclusiveBetween(1, CommerceLimits.MaxPage)
                .WithMessage($"Số trang phải từ 1 đến {CommerceLimits.MaxPage}.");
            RuleFor(query => query.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("Số bản ghi mỗi trang phải từ 1 đến 100.");
        }
    }

    public sealed class CustomerPageQueryParamsValidator
        : AbstractValidator<CustomerPageQueryParams>
    {
        public CustomerPageQueryParamsValidator()
        {
            RuleFor(query => query.Page)
                .InclusiveBetween(1, CommerceLimits.MaxPage)
                .WithMessage($"Số trang phải từ 1 đến {CommerceLimits.MaxPage}.");
            RuleFor(query => query.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("Số bản ghi mỗi trang phải từ 1 đến 100.");
        }
    }
}
