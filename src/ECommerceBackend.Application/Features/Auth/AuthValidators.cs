using ECommerceBackend.Application.DTOs;
using FluentValidation;

namespace ECommerceBackend.Application.Validation
{
    public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
    {
        public RegisterRequestValidator()
        {
            RuleFor(x => x.UserName)
                .NotEmpty().WithMessage("Tên đăng nhập không được để trống.")
                .MinimumLength(3).WithMessage("Tên đăng nhập phải có ít nhất 3 ký tự.")
                .MaximumLength(50).WithMessage("Tên đăng nhập không được vượt quá 50 ký tự.")
                .Matches("^[a-zA-Z0-9_]+$").WithMessage("Tên đăng nhập chỉ được chứa chữ, số và dấu gạch dưới.");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email không được để trống.")
                .MaximumLength(254).WithMessage("Email không được vượt quá 254 ký tự.")
                .EmailAddress().WithMessage("Email không hợp lệ.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Mật khẩu không được để trống.")
                .MinimumLength(12).WithMessage("Mật khẩu phải có ít nhất 12 ký tự.")
                .MaximumLength(128).WithMessage("Mật khẩu không được vượt quá 128 ký tự.");

            RuleFor(x => x.FullName)
                .NotEmpty().WithMessage("Họ tên không được để trống.")
                .MaximumLength(100).WithMessage("Họ tên không được vượt quá 100 ký tự.");

            RuleFor(x => x.Phone)
                .Matches(@"^(\+84|0)[0-9]{9}$").WithMessage("Số điện thoại không hợp lệ.")
                .When(x => !string.IsNullOrEmpty(x.Phone));
        }
    }

    public class LoginRequestValidator : AbstractValidator<LoginRequest>
    {
        public LoginRequestValidator()
        {
            RuleFor(x => x.UserName)
                .NotEmpty().WithMessage("Tên đăng nhập không được để trống.")
                .MaximumLength(50).WithMessage("Tên đăng nhập không được vượt quá 50 ký tự.");
            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Mật khẩu không được để trống.")
                .MaximumLength(128).WithMessage("Mật khẩu không được vượt quá 128 ký tự.");
        }
    }

    public class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
    {
        public RefreshTokenRequestValidator()
        {
            RuleFor(x => x.RefreshToken)
                .NotEmpty().WithMessage("Mã làm mới phiên không được để trống.")
                .MaximumLength(256).WithMessage("Mã làm mới phiên không hợp lệ.");
        }
    }

    public class LogoutRequestValidator : AbstractValidator<LogoutRequest>
    {
        public LogoutRequestValidator()
        {
            RuleFor(x => x.RefreshToken)
                .NotEmpty().WithMessage("Mã làm mới phiên không được để trống.")
                .MaximumLength(256).WithMessage("Mã làm mới phiên không hợp lệ.");
        }
    }

    public sealed class ForgotPasswordRequestValidator
        : AbstractValidator<ForgotPasswordRequest>
    {
        public ForgotPasswordRequestValidator()
        {
            RuleFor(request => request.Email)
                .NotEmpty().WithMessage("Email không được để trống.")
                .MaximumLength(254).WithMessage("Email không được vượt quá 254 ký tự.")
                .EmailAddress().WithMessage("Email không hợp lệ.");
        }
    }

    public sealed class ResetPasswordRequestValidator
        : AbstractValidator<ResetPasswordRequest>
    {
        public ResetPasswordRequestValidator()
        {
            RuleFor(request => request.Token)
                .NotEmpty().WithMessage("Mã đặt lại mật khẩu không được để trống.")
                .MaximumLength(512).WithMessage("Mã đặt lại mật khẩu không hợp lệ.");
            RuleFor(request => request.NewPassword)
                .NotEmpty().WithMessage("Mật khẩu mới không được để trống.")
                .MinimumLength(12).WithMessage("Mật khẩu mới phải có ít nhất 12 ký tự.")
                .MaximumLength(128).WithMessage("Mật khẩu mới không được vượt quá 128 ký tự.");
        }
    }
}
