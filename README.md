# ECommerceBackend

[![Backend CI](https://github.com/Giapnocap/ECommerceBackend/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Giapnocap/ECommerceBackend/actions/workflows/ci.yml)

REST API cho hệ thống thương mại điện tử, xây dựng bằng ASP.NET Core 8, Entity Framework Core và SQL Server. Dự án chỉ gồm backend, không có frontend.

## Chức năng chính

- Đăng ký, đăng nhập, refresh token, quên mật khẩu, xác minh email
- Phân quyền Admin, Staff, Customer
- Quản lý danh mục, sản phẩm, ảnh và tồn kho
- Tìm kiếm, lọc, phân trang sản phẩm; giỏ hàng
- Báo giá, mã khuyến mãi và checkout chống đặt trùng bằng `Idempotency-Key`
- Vòng đời đơn hàng: xác nhận, giao hàng, hủy, trả hàng, hoàn tiền
- Thanh toán COD, có adapter Stripe
- Báo cáo, audit log, gửi email qua transactional outbox

## Hướng dẫn cài đặt

Yêu cầu: Git, Docker Desktop.

```powershell
git clone https://github.com/Giapnocap/ECommerceBackend.git
Set-Location ECommerceBackend
Copy-Item .env.example .env
```

Mở `.env`, đặt giá trị cho `MSSQL_SA_PASSWORD` và `JWT_KEY`, sau đó chạy:

```powershell
docker compose up --build
```

Docker Compose sẽ khởi động SQL Server, chạy migration, rồi mới khởi động API.

### Tạo Admin đầu tiên

Trước lần chạy đầu, điền các biến sau trong `.env`:

```env
ADMIN_BOOTSTRAP_ENABLED=true
ADMIN_BOOTSTRAP_USERNAME=admin
ADMIN_BOOTSTRAP_EMAIL=admin@example.com
ADMIN_BOOTSTRAP_FULL_NAME=Administrator
ADMIN_BOOTSTRAP_PASSWORD=YourSecurePassword123!
```

Sau khi Admin đã được tạo, đổi `ADMIN_BOOTSTRAP_ENABLED=false` và khởi động lại API. Không commit
file `.env` hoặc dùng các giá trị mẫu này trên môi trường thật.

Dừng ứng dụng:

```powershell
docker compose down
```

## Cách sử dụng

- [Swagger UI](http://localhost:5171/swagger)
- [Health check](http://localhost:5171/health/ready)
- Route chuẩn: `/api/v1`

Luồng thử nhanh:

1. Admin tạo danh mục và sản phẩm
2. Customer thêm sản phẩm vào giỏ
3. Customer lấy báo giá tại `POST /api/v1/orders/quote`
4. Customer đặt hàng tại `POST /api/v1/orders` kèm header `Idempotency-Key`
5. Staff xác nhận và giao hàng

Request mẫu: [ECommerceBackend.http](src/ECommerceBackend/ECommerceBackend.http)

## Công nghệ

- **Backend:** .NET 8, ASP.NET Core Web API, Entity Framework Core, FluentValidation
- **Database:** SQL Server 2022
- **Xác thực:** JWT, BCrypt
- **Kiểm thử:** xUnit
- **Công cụ:** Docker Compose, GitHub Actions, Swagger

## Kiểm thử

```powershell
dotnet test tests/ECommerceBackend.UnitTests/ECommerceBackend.UnitTests.csproj
```

Kết quả CI mới nhất xem tại tab [Actions](https://github.com/Giapnocap/ECommerceBackend/actions).

## Giới hạn

- Chạy một API instance và một SQL Server, ảnh lưu trên local volume
- Stripe, SMTP chưa được thử với tài khoản dịch vụ thật

Chi tiết: [docs/LIMITATIONS.md](docs/LIMITATIONS.md)

## Tài liệu

- [Kiến trúc](docs/ARCHITECTURE.md)
- [Sơ đồ database (ERD)](docs/ERD.md)
- [Sequence nghiệp vụ](docs/SEQUENCES.md)
