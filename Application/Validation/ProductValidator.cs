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
                .LessThanOrEqualTo(CommerceLimits.MaxMoneyAmount).WithMessage("Gia san pham vuot gioi han cho phep.")
                .PrecisionScale(CommerceLimits.MoneyPrecision, CommerceLimits.MoneyScale, true)
                .WithMessage("Gia san pham chi duoc co toi da 2 chu so thap phan.");

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
                .LessThanOrEqualTo(CommerceLimits.MaxMoneyAmount).WithMessage("Gia san pham vuot gioi han cho phep.")
                .PrecisionScale(CommerceLimits.MoneyPrecision, CommerceLimits.MoneyScale, true)
                .WithMessage("Gia san pham chi duoc co toi da 2 chu so thap phan.");

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
                .MaximumLength(100).WithMessage("Tu khoa khong duoc vuot qua 100 ky tu.")
                .When(x => x.Keyword != null);

            RuleFor(x => x.MinPrice)
                .GreaterThanOrEqualTo(0).WithMessage("Giá tối thiểu không được âm.")
                .When(x => x.MinPrice.HasValue);

            RuleFor(x => x.MinPrice)
                .LessThanOrEqualTo(CommerceLimits.MaxMoneyAmount).WithMessage("Gia toi thieu vuot gioi han cho phep.")
                .PrecisionScale(CommerceLimits.MoneyPrecision, CommerceLimits.MoneyScale, true)
                .WithMessage("Gia toi thieu chi duoc co toi da 2 chu so thap phan.")
                .When(x => x.MinPrice.HasValue);

            RuleFor(x => x.MaxPrice)
                .GreaterThanOrEqualTo(0).WithMessage("Gia toi da khong duoc am.")
                .LessThanOrEqualTo(CommerceLimits.MaxMoneyAmount).WithMessage("Gia toi da vuot gioi han cho phep.")
                .PrecisionScale(CommerceLimits.MoneyPrecision, CommerceLimits.MoneyScale, true)
                .WithMessage("Gia toi da chi duoc co toi da 2 chu so thap phan.")
                .When(x => x.MaxPrice.HasValue);

            RuleFor(x => x.MaxPrice)
                .GreaterThanOrEqualTo(0).WithMessage("Giá tối đa không được âm.")
                .GreaterThanOrEqualTo(x => x.MinPrice)
                .WithMessage("Giá tối đa phải lớn hơn hoặc bằng giá tối thiểu.")
                .When(x => x.MaxPrice.HasValue && x.MinPrice.HasValue);

            RuleFor(x => x.SortBy)
                .Must(value => value == null || AllowedSortFields.Contains(value.ToLowerInvariant()))
                .WithMessage("SortBy chỉ chấp nhận: name, price, createdAt.");

            RuleFor(x => x.SortOrder)
                .Must(value => value == null || AllowedSortOrders.Contains(value.ToLowerInvariant()))
                .WithMessage("SortOrder chỉ chấp nhận: asc, desc.");

            RuleFor(x => x.Page)
                .GreaterThan(0).WithMessage("Page phải lớn hơn 0.");

            RuleFor(x => x.Page)
                .LessThanOrEqualTo(CommerceLimits.MaxPage).WithMessage($"Page phai tu 1 den {CommerceLimits.MaxPage}.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100).WithMessage("PageSize phải từ 1 đến 100.");
        }
    }
}
