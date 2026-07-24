using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using FluentValidation;

namespace ECommerceBackend.Application.Validation
{
    public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
    {
        public CreateProductRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Tên sản phẩm không được để trống.")
                .MaximumLength(200).WithMessage("Tên sản phẩm không được vượt quá 200 ký tự.");

            RuleFor(x => x.Price)
                .GreaterThan(0).WithMessage("Giá sản phẩm phải lớn hơn 0.");

            RuleFor(x => x.Price)
                .LessThanOrEqualTo(CommerceLimits.MaxMoneyAmount).WithMessage("Giá sản phẩm vượt quá giới hạn cho phép.")
                .PrecisionScale(CommerceLimits.MoneyPrecision, CommerceLimits.MoneyScale, true)
                .WithMessage("Giá sản phẩm chỉ được có tối đa 2 chữ số thập phân.");

            RuleFor(x => x.StockQuantity)
                .GreaterThanOrEqualTo(0).WithMessage("Số lượng tồn kho không được âm.");

            RuleFor(x => x.CategoryId)
                .NotEmpty().WithMessage("Danh mục không được để trống.");

            RuleFor(x => x.Description)
                .MaximumLength(2000).WithMessage("Mô tả không được vượt quá 2000 ký tự.");
        }
    }

    public class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
    {
        public UpdateProductRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Tên sản phẩm không được để trống.")
                .MaximumLength(200).WithMessage("Tên sản phẩm không được vượt quá 200 ký tự.");

            RuleFor(x => x.Price)
                .GreaterThan(0).WithMessage("Giá sản phẩm phải lớn hơn 0.");

            RuleFor(x => x.Price)
                .LessThanOrEqualTo(CommerceLimits.MaxMoneyAmount).WithMessage("Giá sản phẩm vượt quá giới hạn cho phép.")
                .PrecisionScale(CommerceLimits.MoneyPrecision, CommerceLimits.MoneyScale, true)
                .WithMessage("Giá sản phẩm chỉ được có tối đa 2 chữ số thập phân.");

            RuleFor(x => x.StockQuantity)
                .GreaterThanOrEqualTo(0).WithMessage("Số lượng tồn kho không được âm.");

            RuleFor(x => x.CategoryId)
                .NotEmpty().WithMessage("Danh mục không được để trống.");

            RuleFor(x => x.Description)
                .MaximumLength(2000).WithMessage("Mô tả không được vượt quá 2000 ký tự.");
        }
    }

    public class ProductQueryParamsValidator : AbstractValidator<ProductQueryParams>
    {
        private static readonly string[] AllowedSortFields = ["name", "price", "createdat"];
        private static readonly string[] AllowedSortOrders = ["asc", "desc"];

        public ProductQueryParamsValidator()
        {
            RuleFor(x => x.Keyword)
                .MaximumLength(100).WithMessage("Từ khóa không được vượt quá 100 ký tự.")
                .When(x => x.Keyword != null);

            RuleFor(x => x.MinPrice)
                .GreaterThanOrEqualTo(0).WithMessage("Giá tối thiểu không được âm.")
                .When(x => x.MinPrice.HasValue);

            RuleFor(x => x.MinPrice)
                .LessThanOrEqualTo(CommerceLimits.MaxMoneyAmount).WithMessage("Giá tối thiểu vượt quá giới hạn cho phép.")
                .PrecisionScale(CommerceLimits.MoneyPrecision, CommerceLimits.MoneyScale, true)
                .WithMessage("Giá tối thiểu chỉ được có tối đa 2 chữ số thập phân.")
                .When(x => x.MinPrice.HasValue);

            RuleFor(x => x.MaxPrice)
                .GreaterThanOrEqualTo(0).WithMessage("Giá tối đa không được âm.")
                .LessThanOrEqualTo(CommerceLimits.MaxMoneyAmount).WithMessage("Giá tối đa vượt quá giới hạn cho phép.")
                .PrecisionScale(CommerceLimits.MoneyPrecision, CommerceLimits.MoneyScale, true)
                .WithMessage("Giá tối đa chỉ được có tối đa 2 chữ số thập phân.")
                .When(x => x.MaxPrice.HasValue);

            RuleFor(x => x.MaxPrice)
                .GreaterThanOrEqualTo(0).WithMessage("Giá tối đa không được âm.")
                .GreaterThanOrEqualTo(x => x.MinPrice)
                .WithMessage("Giá tối đa phải lớn hơn hoặc bằng giá tối thiểu.")
                .When(x => x.MaxPrice.HasValue && x.MinPrice.HasValue);

            RuleFor(x => x.SortBy)
                .Must(value => value == null || AllowedSortFields.Contains(value.ToLowerInvariant()))
                .WithMessage("Tiêu chí sắp xếp chỉ chấp nhận: tên, giá hoặc ngày tạo.");

            RuleFor(x => x.SortOrder)
                .Must(value => value == null || AllowedSortOrders.Contains(value.ToLowerInvariant()))
                .WithMessage("Thứ tự sắp xếp chỉ chấp nhận tăng dần hoặc giảm dần.");

            RuleFor(x => x.Page)
                .GreaterThan(0).WithMessage("Số trang phải lớn hơn 0.");

            RuleFor(x => x.Page)
                .LessThanOrEqualTo(CommerceLimits.MaxPage).WithMessage($"Số trang phải từ 1 đến {CommerceLimits.MaxPage}.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100).WithMessage("Số bản ghi mỗi trang phải từ 1 đến 100.");
        }
    }
}
