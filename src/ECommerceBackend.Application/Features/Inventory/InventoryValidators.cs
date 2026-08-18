using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Enums;
using FluentValidation;

namespace ECommerceBackend.Application.Validation
{
    public sealed class AdjustProductStockRequestValidator : AbstractValidator<AdjustProductStockRequest>
    {
        public AdjustProductStockRequestValidator()
        {
            RuleFor(x => x.TargetQuantity)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Tồn kho mục tiêu không được âm.");
            RuleFor(x => x.Reason)
                .NotEmpty()
                .WithMessage("Lý do điều chỉnh tồn kho không được để trống.")
                .MaximumLength(500)
                .WithMessage("Lý do điều chỉnh tồn kho không được vượt quá 500 ký tự.");
            RuleFor(x => x.Reference)
                .MaximumLength(200)
                .WithMessage("Mã tham chiếu tồn kho không được vượt quá 200 ký tự.");
        }
    }

    public sealed class StockInRequestValidator : AbstractValidator<StockInRequest>
    {
        public StockInRequestValidator()
        {
            RuleFor(x => x.Quantity)
                .InclusiveBetween(1, 1_000_000)
                .WithMessage("Số lượng nhập kho phải từ 1 đến 1.000.000.");
            RuleFor(x => x.Reference)
                .MaximumLength(200)
                .WithMessage("Mã tham chiếu tồn kho không được vượt quá 200 ký tự.");
            RuleFor(x => x.Reason)
                .NotEmpty()
                .WithMessage("Lý do nhập kho không được để trống.")
                .MaximumLength(500)
                .WithMessage("Lý do nhập kho không được vượt quá 500 ký tự.");
        }
    }

    public sealed class UpdateLowStockThresholdRequestValidator
        : AbstractValidator<UpdateLowStockThresholdRequest>
    {
        public UpdateLowStockThresholdRequestValidator()
        {
            RuleFor(x => x.Threshold)
                .InclusiveBetween(0, 1_000_000)
                .WithMessage("Ngưỡng tồn kho phải từ 0 đến 1.000.000.");
        }
    }

    public class InventoryQueryParamsValidator : AbstractValidator<InventoryQueryParams>
    {
        public InventoryQueryParamsValidator()
        {
            RuleFor(x => x.Page)
                .InclusiveBetween(1, CommerceLimits.MaxPage)
                .WithMessage($"Số trang phải từ 1 đến {CommerceLimits.MaxPage}.");
            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("Số bản ghi mỗi trang phải từ 1 đến 100.");
            RuleFor(x => x.Type)
                .Must(value => string.IsNullOrWhiteSpace(value)
                    || Enum.TryParse<InventoryTransactionType>(
                        value,
                        ignoreCase: true,
                        out var parsed)
                    && Enum.IsDefined(parsed))
                .WithMessage("Loại giao dịch tồn kho không hợp lệ.");
            RuleFor(x => x.To)
                .GreaterThan(x => x.From)
                .When(x => x.From.HasValue && x.To.HasValue)
                .WithMessage("Thời điểm kết thúc phải lớn hơn thời điểm bắt đầu.");
        }
    }

    public class LowStockQueryParamsValidator : AbstractValidator<LowStockQueryParams>
    {
        public LowStockQueryParamsValidator()
        {
            Include(new InventoryQueryParamsValidator());
            RuleFor(x => x.Threshold)
                .InclusiveBetween(0, 1_000_000)
                .WithMessage("Ngưỡng tồn kho phải từ 0 đến 1.000.000.");
        }
    }

    public sealed class InventoryProductQueryParamsValidator
        : AbstractValidator<InventoryProductQueryParams>
    {
        private static readonly string[] AllowedSortFields = ["name", "stock", "createdat"];
        private static readonly string[] AllowedSortOrders = ["asc", "desc"];

        public InventoryProductQueryParamsValidator()
        {
            RuleFor(x => x.Keyword)
                .MaximumLength(100)
                .When(x => x.Keyword != null)
                .WithMessage("Từ khóa không được vượt quá 100 ký tự.");
            RuleFor(x => x.LowStockThreshold)
                .InclusiveBetween(0, 1_000_000)
                .When(x => x.LowStockThreshold.HasValue)
                .WithMessage("Ngưỡng tồn kho phải từ 0 đến 1.000.000.");
            RuleFor(x => x.SortBy)
                .Must(value => value == null
                    || AllowedSortFields.Contains(value.ToLowerInvariant()))
                .WithMessage("Tiêu chí sắp xếp chỉ chấp nhận: tên, tồn kho hoặc ngày tạo.");
            RuleFor(x => x.SortOrder)
                .Must(value => value == null
                    || AllowedSortOrders.Contains(value.ToLowerInvariant()))
                .WithMessage("Thứ tự sắp xếp chỉ chấp nhận tăng dần hoặc giảm dần.");
            RuleFor(x => x.Page)
                .InclusiveBetween(1, CommerceLimits.MaxPage)
                .WithMessage($"Số trang phải từ 1 đến {CommerceLimits.MaxPage}.");
            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("Số bản ghi mỗi trang phải từ 1 đến 100.");
        }
    }
}
