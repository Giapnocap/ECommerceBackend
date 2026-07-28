using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Enums;
using FluentValidation;

namespace ECommerceBackend.Application.Validation
{
    public sealed class CreatePromotionRequestValidator
        : AbstractValidator<CreatePromotionRequest>
    {
        public CreatePromotionRequestValidator()
        {
            RuleFor(request => request.Code)
                .NotEmpty()
                .Matches("^[A-Za-z0-9_-]{3,32}$")
                .WithMessage(
                    "Mã khuyến mãi phải có 3-32 ký tự gồm chữ cái, số, dấu gạch ngang hoặc gạch dưới.");
            PromotionValidationRules.Add(this);
        }
    }

    public sealed class UpdatePromotionRequestValidator
        : AbstractValidator<UpdatePromotionRequest>
    {
        public UpdatePromotionRequestValidator()
        {
            PromotionValidationRules.Add(this);
        }
    }

    public sealed class PromotionQueryParamsValidator
        : AbstractValidator<PromotionQueryParams>
    {
        public PromotionQueryParamsValidator()
        {
            RuleFor(request => request.Page)
                .InclusiveBetween(1, CommerceLimits.MaxPage)
                .WithMessage(
                    $"Số trang phải từ 1 đến {CommerceLimits.MaxPage}.");
            RuleFor(request => request.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("Số bản ghi mỗi trang phải từ 1 đến 100.");
        }
    }

    internal static class PromotionValidationRules
    {
        public static void Add<T>(AbstractValidator<T> validator)
            where T : IPromotionRuleRequest
        {
            validator.RuleFor(request => request.Type)
                .IsInEnum()
                .WithMessage("Loại khuyến mãi không hợp lệ.");
            validator.RuleFor(request => request.Value)
                .GreaterThan(0)
                .LessThanOrEqualTo(CommerceLimits.MaxMoneyAmount)
                .PrecisionScale(
                    CommerceLimits.MoneyPrecision,
                    CommerceLimits.MoneyScale,
                    true)
                .WithMessage(
                    "Giá trị khuyến mãi phải lớn hơn 0 và có tối đa 2 chữ số thập phân.");
            validator.RuleFor(request => request.MinimumSubtotal)
                .GreaterThanOrEqualTo(0)
                .LessThanOrEqualTo(CommerceLimits.MaxMoneyAmount)
                .PrecisionScale(
                    CommerceLimits.MoneyPrecision,
                    CommerceLimits.MoneyScale,
                    true)
                .WithMessage(
                    "Tạm tính tối thiểu phải hợp lệ và có tối đa 2 chữ số thập phân.");
            validator.RuleFor(request =>
                    request.MaximumDiscountAmount)
                .GreaterThan(0)
                .LessThanOrEqualTo(CommerceLimits.MaxMoneyAmount)
                .PrecisionScale(
                    CommerceLimits.MoneyPrecision,
                    CommerceLimits.MoneyScale,
                    true)
                .When(request =>
                    request.MaximumDiscountAmount.HasValue)
                .WithMessage(
                    "Mức giảm tối đa phải lớn hơn 0 và có tối đa 2 chữ số thập phân.");
            validator.RuleFor(request => request.EndsAt)
                .GreaterThan(request => request.StartsAt)
                .WithMessage("Thời gian kết thúc phải sau thời gian bắt đầu.");
            validator.RuleFor(request => request.StartsAt)
                .Must(value => value.Kind == DateTimeKind.Utc)
                .WithMessage("Thời gian bắt đầu phải sử dụng UTC.");
            validator.RuleFor(request => request.EndsAt)
                .Must(value => value.Kind == DateTimeKind.Utc)
                .WithMessage("Thời gian kết thúc phải sử dụng UTC.");
            validator.RuleFor(request => request.UsageLimit)
                .InclusiveBetween(1, 1_000_000)
                .WithMessage("Giới hạn sử dụng phải từ 1 đến 1.000.000.");
            validator.RuleFor(request =>
                    request.UsageLimitPerCustomer)
                .InclusiveBetween(1, 10_000)
                .WithMessage(
                    "Giới hạn sử dụng mỗi khách phải từ 1 đến 10.000.");
        }
    }
}
