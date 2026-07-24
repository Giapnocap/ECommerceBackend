using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using FluentValidation;

namespace ECommerceBackend.Application.Validation
{
    public class AddToCartRequestValidator : AbstractValidator<AddToCartRequest>
    {
        public AddToCartRequestValidator()
        {
            RuleFor(x => x.ProductId)
                .NotEmpty().WithMessage("Sản phẩm không được để trống.");

            RuleFor(x => x.Quantity)
                .GreaterThan(0).WithMessage("Số lượng phải lớn hơn 0.");
        }
    }

    public class UpdateCartItemRequestValidator : AbstractValidator<UpdateCartItemRequest>
    {
        public UpdateCartItemRequestValidator()
        {
            RuleFor(x => x.Quantity)
                .GreaterThanOrEqualTo(0).WithMessage("Số lượng phải lớn hơn hoặc bằng 0.");
        }
    }

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
        }
    }

    public class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
    {
        public CreateCategoryRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Tên danh mục không được để trống.")
                .MaximumLength(100).WithMessage("Tên danh mục không được vượt quá 100 ký tự.");
        }
    }

    public class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
    {
        public UpdateCategoryRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Tên danh mục không được để trống.")
                .MaximumLength(100).WithMessage("Tên danh mục không được vượt quá 100 ký tự.");
        }
    }

    public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
    {
        public UpdateProfileRequestValidator()
        {
            RuleFor(x => x.FullName)
                .NotEmpty().WithMessage("Họ tên không được để trống.")
                .MaximumLength(100).WithMessage("Họ tên không được vượt quá 100 ký tự.");

            RuleFor(x => x.Phone)
                .Matches(@"^(\+84|0)[0-9]{9}$").WithMessage("Số điện thoại không hợp lệ.")
                .When(x => !string.IsNullOrEmpty(x.Phone));
        }
    }

    public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
    {
        public ChangePasswordRequestValidator()
        {
            RuleFor(x => x.CurrentPassword)
                .NotEmpty().WithMessage("Mật khẩu hiện tại không được để trống.")
                .MaximumLength(128).WithMessage("Mật khẩu hiện tại không hợp lệ.");

            RuleFor(x => x.NewPassword)
                .NotEmpty().WithMessage("Mật khẩu mới không được để trống.")
                .MinimumLength(12).WithMessage("Mật khẩu mới phải có ít nhất 12 ký tự.")
                .MaximumLength(128).WithMessage("Mật khẩu mới không được vượt quá 128 ký tự.");
        }
    }

    public class AssignRoleRequestValidator : AbstractValidator<AssignRoleRequest>
    {
        public AssignRoleRequestValidator()
        {
            RuleFor(x => x.RoleName)
                .NotEmpty().WithMessage("Vai trò không được để trống.")
                .Must(RoleNames.IsValid)
                .WithMessage("Vai trò không hợp lệ.");
        }
    }

    public class UserQueryParamsValidator : AbstractValidator<UserQueryParams>
    {
        public UserQueryParamsValidator()
        {
            RuleFor(x => x.Keyword)
                .MaximumLength(100).WithMessage("Từ khóa không được vượt quá 100 ký tự.")
                .When(x => x.Keyword != null);

            RuleFor(x => x.Role)
                .NotEmpty().WithMessage("Vai trò không được để trống.")
                .Must(role => RoleNames.IsValid(role))
                .WithMessage("Vai trò không hợp lệ.")
                .When(x => x.Role != null);

            RuleFor(x => x.Page)
                .InclusiveBetween(1, CommerceLimits.MaxPage)
                .WithMessage($"Số trang phải từ 1 đến {CommerceLimits.MaxPage}.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("Số bản ghi mỗi trang phải từ 1 đến 100.");
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
                .LessThanOrEqualTo(CommerceLimits.MaxPage).WithMessage($"Số trang phải từ 1 đến {CommerceLimits.MaxPage}.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100).WithMessage("Số bản ghi mỗi trang phải từ 1 đến 100.");
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

    public sealed class DeadLetterQueryParamsValidator : AbstractValidator<DeadLetterQueryParams>
    {
        public DeadLetterQueryParamsValidator()
        {
            RuleFor(x => x.Page).InclusiveBetween(1, CommerceLimits.MaxPage).WithMessage($"Số trang phải từ 1 đến {CommerceLimits.MaxPage}.");
            RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("Số bản ghi mỗi trang phải từ 1 đến 100.");
        }
    }

    public sealed class AuditQueryParamsValidator : AbstractValidator<AuditQueryParams>
    {
        public AuditQueryParamsValidator()
        {
            RuleFor(x => x.Page).InclusiveBetween(1, CommerceLimits.MaxPage).WithMessage($"Số trang phải từ 1 đến {CommerceLimits.MaxPage}.");
            RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("Số bản ghi mỗi trang phải từ 1 đến 100.");
            RuleFor(x => x.Action).MaximumLength(100).WithMessage("Hành động không được vượt quá 100 ký tự.").When(x => x.Action != null);
            RuleFor(x => x.EntityType).MaximumLength(100).WithMessage("Loại đối tượng không được vượt quá 100 ký tự.").When(x => x.EntityType != null);
            RuleFor(x => x.To)
                .GreaterThan(x => x.From)
                .WithMessage("Thời điểm kết thúc phải lớn hơn thời điểm bắt đầu.")
                .When(x => x.From.HasValue && x.To.HasValue);
        }
    }

    public sealed class UploadReconciliationRequestValidator : AbstractValidator<UploadReconciliationRequest>
    {
        public UploadReconciliationRequestValidator()
        {
            RuleFor(x => x.MaxDeletes).InclusiveBetween(1, 100).WithMessage("Số tệp xóa tối đa phải từ 1 đến 100.");
        }
    }

    public sealed class DataRetentionRequestValidator : AbstractValidator<DataRetentionRequest>
    {
        public DataRetentionRequestValidator()
        {
            RuleFor(x => x.MaxBatchSize)
                .InclusiveBetween(1, 500)
                .WithMessage("Số bản ghi xử lý mỗi lô phải từ 1 đến 500.");
        }
    }
}
