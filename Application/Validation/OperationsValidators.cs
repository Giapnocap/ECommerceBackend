using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using FluentValidation;

namespace ECommerceBackend.Application.Validation
{
    public sealed class DeadLetterQueryParamsValidator : AbstractValidator<DeadLetterQueryParams>
    {
        public DeadLetterQueryParamsValidator()
        {
            RuleFor(x => x.Page)
                .InclusiveBetween(1, CommerceLimits.MaxPage)
                .WithMessage($"Số trang phải từ 1 đến {CommerceLimits.MaxPage}.");
            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("Số bản ghi mỗi trang phải từ 1 đến 100.");
        }
    }

    public sealed class AuditQueryParamsValidator : AbstractValidator<AuditQueryParams>
    {
        public AuditQueryParamsValidator()
        {
            RuleFor(x => x.Page)
                .InclusiveBetween(1, CommerceLimits.MaxPage)
                .WithMessage($"Số trang phải từ 1 đến {CommerceLimits.MaxPage}.");
            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("Số bản ghi mỗi trang phải từ 1 đến 100.");
            RuleFor(x => x.Action)
                .MaximumLength(100)
                .WithMessage("Hành động không được vượt quá 100 ký tự.")
                .When(x => x.Action != null);
            RuleFor(x => x.EntityType)
                .MaximumLength(100)
                .WithMessage("Loại đối tượng không được vượt quá 100 ký tự.")
                .When(x => x.EntityType != null);
            RuleFor(x => x.To)
                .GreaterThan(x => x.From)
                .WithMessage("Thời điểm kết thúc phải lớn hơn thời điểm bắt đầu.")
                .When(x => x.From.HasValue && x.To.HasValue);
        }
    }

    public sealed class UploadReconciliationRequestValidator :
        AbstractValidator<UploadReconciliationRequest>
    {
        public UploadReconciliationRequestValidator()
        {
            RuleFor(x => x.MaxDeletes)
                .InclusiveBetween(1, 100)
                .WithMessage("Số tệp xóa tối đa phải từ 1 đến 100.");
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
