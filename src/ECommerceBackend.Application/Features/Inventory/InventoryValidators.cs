using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
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
}
