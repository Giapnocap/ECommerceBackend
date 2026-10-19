# ECommerceBackend

[![Backend CI](https://github.com/Giapnocap/ECommerceBackend/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Giapnocap/ECommerceBackend/actions/workflows/ci.yml)

REST API cho hệ thống thương mại điện tử, xây dựng bằng ASP.NET Core 8, Entity Framework Core và SQL Server theo kiến trúc Clean Architecture.

## Chức năng chính

- Đăng ký, đăng nhập, refresh token
- Phân quyền Admin, Staff, Customer
- Quản lý danh mục, sản phẩm, ảnh và tồn kho
- Tìm kiếm, lọc, phân trang sản phẩm; giỏ hàng
- Báo giá, mã khuyến mãi và checkout bằng `Idempotency-Key`
- Vòng đời đơn hàng: xác nhận, giao hàng, hủy, trả hàng và hoàn tồn kho
- Thanh toán COD, webhook HMAC
- Báo cáo thống kê, audit log và background worker

## Hướng dẫn cài đặt

Yêu cầu: Git, Docker Desktop (hoặc .NET 8 SDK, SQL Server).

```powershell
git clone https://github.com/Giapnocap/ECommerceBackend.git
Set-Location ECommerceBackend
Copy-Item .env.example .env
```

Mở `.env` và đặt giá trị cho `MSSQL_SA_PASSWORD`, `JWT_KEY` và tài khoản Admin ban đầu:

```env
ADMIN_BOOTSTRAP_ENABLED=true
ADMIN_BOOTSTRAP_USERNAME=admin
ADMIN_BOOTSTRAP_EMAIL=admin@example.com
ADMIN_BOOTSTRAP_FULL_NAME=Administrator
ADMIN_BOOTSTRAP_PASSWORD=change-me-admin-password-123!
```

Chạy bằng Docker:
```powershell
docker compose up --build
```

*(Sau khi tạo Admin thành công, đổi `ADMIN_BOOTSTRAP_ENABLED=false` trong `.env` và chạy lại `docker compose up -d --force-recreate api`)*.

Chạy trực tiếp trên máy (.NET CLI):
```powershell
dotnet ef database update --project src/ECommerceBackend.Infrastructure --startup-project src/ECommerceBackend
dotnet run --project src/ECommerceBackend
```

## Cách sử dụng

- Swagger UI: `http://localhost:5171/swagger`
- Health check: `/health/live` và `/health/ready`
- Route chuẩn: `/api/v1`
- Request mẫu: [ECommerceBackend.http](src/ECommerceBackend/ECommerceBackend.http)

Khởi tạo dữ liệu mẫu (chỉ trên Development/Local):
```powershell
sqlcmd -S localhost,1433 -d ECommerceDB -U sa -P $env:MSSQL_SA_PASSWORD -i scripts/SeedDemoData.sql -v EnvironmentName=Development
```

Luồng thử nhanh:
1. Admin tạo danh mục và sản phẩm
2. Customer thêm sản phẩm vào giỏ
3. Customer lấy báo giá tại `POST /api/v1/orders/quote`
4. Customer đặt hàng tại `POST /api/v1/orders` kèm header `Idempotency-Key`
5. Staff xác nhận và giao hàng

## Kiểm thử

```powershell
dotnet test tests/ECommerceBackend.UnitTests/ECommerceBackend.UnitTests.csproj
```
