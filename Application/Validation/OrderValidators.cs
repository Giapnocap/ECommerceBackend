using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using FluentValidation;

namespace ECommerceBackend.Application.Validation
{
    public class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
    {
        public PlaceOrderRequestValidator()
        {
            RuleFor(x => x.ShippingAddress)
                .NotEmpty().WithMessage("Địa chỉ giao hàng không được để trống.")
                .MaximumLength(500).WithMessage("Địa chỉ không được vượt quá 500 ký tự.");

            RuleFor(x => x.Note)
                .MaximumLength(500).WithMessage("Ghi chú không được vượt quá 500 ký tự.")
                .When(x => x.Note != null);

            RuleFor(x => x.PaymentMethod)
                .IsInEnum().WithMessage("Phương thức thanh toán không hợp lệ.");

            RuleFor(x => x.ShippingMethod)
                .IsInEnum().WithMessage("Phương thức giao hàng không hợp lệ.");

            RuleFor(x => x.PromotionCode)
                .Matches("^[A-Za-z0-9_-]{3,32}$")
                .WithMessage(
                    "Mã khuyến mãi phải có 3-32 ký tự gồm chữ cái, số, dấu gạch ngang hoặc gạch dưới.")
                .When(x => !string.IsNullOrWhiteSpace(x.PromotionCode));

            RuleFor(x => x.ExpectedTotalAmount)
                .GreaterThan(0)
                .LessThanOrEqualTo(CommerceLimits.MaxMoneyAmount)
                .PrecisionScale(
                    CommerceLimits.MoneyPrecision,
                    CommerceLimits.MoneyScale,
                    true)
                .WithMessage(
                    "Tổng tiền dự kiến phải lớn hơn 0 và có tối đa 2 chữ số thập phân.")
                .When(x => x.ExpectedTotalAmount.HasValue);
        }
    }

    public sealed class OrderQuoteRequestValidator
        : AbstractValidator<OrderQuoteRequest>
    {
        public OrderQuoteRequestValidator()
        {
            RuleFor(x => x.ShippingMethod)
                .IsInEnum().WithMessage("Phương thức giao hàng không hợp lệ.");

            RuleFor(x => x.PromotionCode)
                .Matches("^[A-Za-z0-9_-]{3,32}$")
                .WithMessage(
                    "Mã khuyến mãi phải có 3-32 ký tự gồm chữ cái, số, dấu gạch ngang hoặc gạch dưới.")
                .When(x => !string.IsNullOrWhiteSpace(x.PromotionCode));
        }
    }

    public class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
    {
        public UpdateOrderStatusRequestValidator()
        {
            RuleFor(x => x.Status)
                .IsInEnum().WithMessage("Trạng thái đơn hàng không hợp lệ.");

            RuleFor(x => x.Note)
                .MaximumLength(500).WithMessage("Ghi chú trạng thái không được vượt quá 500 ký tự.")
                .When(x => x.Note != null);

            RuleFor(x => x.Note)
                .MaximumLength(200).WithMessage("Lý do hủy đơn không được vượt quá 200 ký tự.")
                .When(x => x.Status == Domain.Enums.OrderStatus.Cancelled && x.Note != null);

            RuleFor(x => x.Note)
                .NotEmpty().WithMessage("Phải nhập lý do giao thất bại hoặc hoàn hàng.")
                .When(x => x.Status is Domain.Enums.OrderStatus.DeliveryFailed
                    or Domain.Enums.OrderStatus.Returned);
        }
    }

    public class CancelOrderRequestValidator : AbstractValidator<CancelOrderRequest>
    {
        public CancelOrderRequestValidator()
        {
            RuleFor(x => x.Reason)
                .MaximumLength(200).WithMessage("Lý do hủy đơn không được vượt quá 200 ký tự.")
                .When(x => x.Reason != null);
        }
    }

    public sealed class RecordOrderRefundRequestValidator : AbstractValidator<RecordOrderRefundRequest>
    {
        public RecordOrderRefundRequestValidator()
        {
            RuleFor(x => x.Reference)
                .NotEmpty().WithMessage("Mã tham chiếu hoàn tiền không được để trống.")
                .MaximumLength(200).WithMessage("Mã tham chiếu hoàn tiền không được vượt quá 200 ký tự.");

            RuleFor(x => x.Note)
                .MaximumLength(500).WithMessage("Ghi chú hoàn tiền không được vượt quá 500 ký tự.")
                .When(x => x.Note != null);
        }
    }

    public class OrderQueryParamsValidator : AbstractValidator<OrderQueryParams>
    {
        public OrderQueryParamsValidator()
        {
            RuleFor(x => x.Status)
                .IsInEnum().WithMessage("Trạng thái đơn hàng không hợp lệ.")
                .When(x => x.Status.HasValue);

            RuleFor(x => x.Page)
                .GreaterThan(0).WithMessage("Số trang phải lớn hơn 0.");

            RuleFor(x => x.Page)
                .LessThanOrEqualTo(CommerceLimits.MaxPage)
                .WithMessage($"Số trang phải từ 1 đến {CommerceLimits.MaxPage}.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100).WithMessage("Số bản ghi mỗi trang phải từ 1 đến 100.");
        }
    }
}
