using AutoMapper;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<User, UserResponse>()
                .ForMember(dest => dest.Roles, opt => opt.MapFrom(src =>
                    src.UserRoles
                        .Where(ur => ur.Role != null)
                        .Select(ur => ur.Role!.Name)));

            CreateMap<Category, CategoryResponse>()
                .ForMember(dest => dest.ParentName, opt => opt.MapFrom(src => src.Parent != null ? src.Parent.Name : null))
                .ForMember(dest => dest.Children, opt => opt.MapFrom(src =>
                    src.Children.Where(child => !child.IsDeleted)));

            CreateMap<ProductImage, ProductImageResponse>();
            CreateMap<ProductImage, UploadImageResponse>();

            CreateMap<Product, ProductResponse>()
                .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category != null ? src.Category.Name : string.Empty));

            CreateMap<CartItem, CartItemResponse>()
                .ForMember(dest => dest.ProductName, opt => opt.MapFrom(src => src.Product != null ? src.Product.Name : string.Empty))
                .ForMember(dest => dest.UnitPrice, opt => opt.MapFrom(src =>
                    src.Product != null && !src.Product.IsDeleted ? src.Product.Price : src.UnitPrice))
                .ForMember(dest => dest.IsAvailable, opt => opt.MapFrom(src =>
                    src.Product != null && !src.Product.IsDeleted && src.Product.StockQuantity >= src.Quantity))
                .ForMember(dest => dest.AvailableStock, opt => opt.MapFrom(src =>
                    src.Product != null && !src.Product.IsDeleted ? src.Product.StockQuantity : 0))
                .ForMember(dest => dest.ProductImageUrl, opt => opt.MapFrom(src =>
                    src.Product == null
                        ? null
                        : src.Product.Images
                            .OrderByDescending(image => image.IsMain)
                            .Select(image => image.ImageUrl)
                            .FirstOrDefault()));

            CreateMap<Cart, CartResponse>()
                .ForMember(dest => dest.Items, opt => opt.MapFrom(src => src.CartItems));

            CreateMap<OrderDetail, OrderDetailResponse>()
                .ForMember(dest => dest.ProductName, opt => opt.MapFrom(src => src.ProductNameSnapshot));

            CreateMap<PaymentStatusHistory, PaymentStatusHistoryResponse>()
                .ForMember(dest => dest.FromStatus, opt => opt.MapFrom(src =>
                    src.FromStatus.HasValue ? src.FromStatus.Value.ToString() : null))
                .ForMember(dest => dest.ToStatus, opt => opt.MapFrom(src => src.ToStatus.ToString()))
                .ForMember(dest => dest.Source, opt => opt.MapFrom(src => src.Source.ToString()));

            CreateMap<Payment, PaymentResponse>()
                .ForMember(dest => dest.Method, opt => opt.MapFrom(src => src.Method.ToString()))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status.ToString()));

            CreateMap<OrderStatusHistory, OrderStatusHistoryResponse>()
                .ForMember(dest => dest.FromStatus, opt => opt.MapFrom(src =>
                    src.FromStatus.HasValue ? src.FromStatus.Value.ToString() : null))
                .ForMember(dest => dest.ToStatus, opt => opt.MapFrom(src => src.ToStatus.ToString()));

            CreateMap<Order, OrderResponse>()
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status.ToString()));
        }
    }
}
