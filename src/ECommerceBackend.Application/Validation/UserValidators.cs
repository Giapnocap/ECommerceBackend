using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using FluentValidation;

namespace ECommerceBackend.Application.Validation
{
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
}
