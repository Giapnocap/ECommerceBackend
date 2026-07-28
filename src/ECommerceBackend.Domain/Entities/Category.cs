using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Domain.Entities
{
    public class Category
    {
        public Guid Id { get; set; }
        public string Name { get; internal set; } = string.Empty;
        public string NormalizedName { get; internal set; } = string.Empty;
        public Guid? ParentId { get; internal set; }
        public bool IsDeleted { get; internal set; }
        public byte[] RowVersion { get; set; } = [];

        // Navigation
        public Category? Parent { get; set; }
        public ICollection<Category> Children { get; set; } = new List<Category>();
        public ICollection<Product> Products { get; set; } = new List<Product>();

        public static Category Create(
            Guid id,
            string name,
            Category? parent)
        {
            if (id == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "category_identity_invalid",
                    "Mã danh mục không hợp lệ.");
            }

            var category = new Category { Id = id };
            category.UpdateDetails(name, parent);
            return category;
        }

        public void UpdateDetails(string name, Category? parent)
        {
            EnsureCanUpdateDetails(name, parent);

            Name = name.Trim();
            NormalizedName = NormalizeName(name);
            ParentId = parent?.Id;
        }

        public void EnsureCanUpdateDetails(string name, Category? parent)
        {
            _ = NormalizeName(name);
            EnsureValidParent(parent);

            if (parent != null
                && Children.Any(child => !child.IsDeleted))
            {
                throw new DomainRuleViolationException(
                    "business_error",
                    "Không thể chuyển danh mục có con thành danh mục con.");
            }
        }

        public bool MarkDeleted()
        {
            if (IsDeleted)
                return false;

            if (Children.Any(child => !child.IsDeleted))
            {
                throw new DomainRuleViolationException(
                    "business_error",
                    "Không thể xóa danh mục đang có danh mục con.");
            }

            if (Products.Any(product => !product.IsDeleted))
            {
                throw new DomainRuleViolationException(
                    "business_error",
                    "Không thể xóa danh mục đang có sản phẩm.");
            }

            IsDeleted = true;
            return true;
        }

        public static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new DomainRuleViolationException(
                    "category_name_invalid",
                    "Tên danh mục không được để trống.");
            }

            var normalizedName = name.Trim();
            if (normalizedName.Length > 100)
            {
                throw new DomainRuleViolationException(
                    "category_name_invalid",
                    "Tên danh mục không được vượt quá 100 ký tự.");
            }

            return normalizedName.ToUpperInvariant();
        }

        private void EnsureValidParent(Category? parent)
        {
            if (parent == null)
                return;

            if (parent.IsDeleted)
            {
                throw new DomainRuleViolationException(
                    "category_parent_unavailable",
                    "Danh mục cha không tồn tại.");
            }

            if (parent.Id == Id)
            {
                throw new DomainRuleViolationException(
                    "business_error",
                    "Danh mục không thể là cha của chính nó.");
            }

            if (parent.ParentId == Id)
            {
                throw new DomainRuleViolationException(
                    "business_error",
                    "Không thể chọn danh mục con làm danh mục cha.");
            }

            if (parent.ParentId.HasValue)
            {
                throw new DomainRuleViolationException(
                    "business_error",
                    "Chỉ hỗ trợ tối đa 2 cấp danh mục.");
            }
        }
    }
}
