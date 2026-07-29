using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Swagger
{
    /// <summary>Dữ liệu lỗi chuẩn trả về từ kiểm tra dữ liệu, quy tắc nghiệp vụ và lớp xử lý ngoại lệ.</summary>
    public sealed class ApiErrorResponse : ProblemDetails
    {
        /// <summary>Thông báo lỗi dành cho người dùng.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Mã lỗi ổn định dành cho ứng dụng gọi API.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Mã truy vết yêu cầu dùng để điều tra sự cố.</summary>
        public string TraceId { get; set; } = string.Empty;

        /// <summary>Thông tin chẩn đoán chỉ xuất hiện trong môi trường phát triển khi có.</summary>
        public string Details { get; set; } = string.Empty;

        /// <summary>Lỗi kiểm tra dữ liệu theo tên thuộc tính của yêu cầu.</summary>
        public IDictionary<string, string[]>? Errors { get; set; }
    }
}
