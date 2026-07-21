# ECommerceBackend

ASP.NET Core 8 Web API cho hệ thống e-commerce, triển khai dưới dạng modular monolith phân tầng:

- `Domain`: entity và enum thuần nghiệp vụ.
- `Application`: DTO, validation, service, interface, exception.
- `Infrastructure`: EF Core DbContext, repository, migrations.
- `API`: controllers, middleware, Swagger, dependency registration.

## Yêu Cầu

- .NET SDK 8.x
- SQL Server local hoặc SQL Server Express
- Visual Studio 2022 hoặc CLI `dotnet`

## Cấu Hình

Connection string mặc định nằm trong `appsettings.json`:

```json
"Default": "Server=.;Database=ECommerceDB;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;"
```

Khi chạy trên máy khác, nên override bằng `appsettings.Local.json` hoặc environment variable. File `appsettings.Local.json` đã được đưa vào `.gitignore`.

JWT signing key không được lưu trong `appsettings.json`. Sao chép `appsettings.Local.example.json` thành `appsettings.Local.json`, đặt một key ngẫu nhiên tối thiểu 32 bytes và không commit tệp đó. Có thể override bằng environment variable.

Giới hạn ảnh sản phẩm được cấu hình bằng `Uploads:MaxImageSizeBytes` (mặc định 5MB). API chấp nhận JPEG, PNG và WebP khi extension, MIME type và nội dung file khớp nhau.

## Chạy Dự Án

Restore/build:

```bash
dotnet restore
dotnet build
```

Áp dụng migration:

```bash
dotnet ef database update
```

Chạy API:

```bash
dotnet run
```

Swagger:

```text
https://localhost:7100/swagger
http://localhost:5171/swagger
```

## Bootstrap Admin

`AdminBootstrap` bị tắt mặc định. Với database local mới, sao chép
`appsettings.Local.example.json` thành `appsettings.Local.json`, thiết lập password riêng,
đặt `AdminBootstrap:Enabled` thành `true`, chạy ứng dụng một lần, sau đó đổi lại `false`.

## Luồng Test Nhanh

1. `POST /api/auth/login` bằng tài khoản admin.
2. Copy token vào Swagger Authorize hoặc biến `@AdminToken` / `@CustomerToken` trong `ECommerceBackend.http`.
3. Tạo category.
4. Tạo product.
5. Upload image cho product.
6. Register/login customer.
7. Customer thêm product vào cart.
8. Customer đặt order.
9. Admin/Staff cập nhật trạng thái order.

## Trạng Thái Hiện Tại

Core MVP đã có:

- Auth register/login bằng JWT.
- Refresh-token family, rotation nguyên tử, phát hiện token reuse, logout và logout-all.
- Role `Admin`, `Staff`, `Customer`.
- User management có lọc theo từ khóa/role và phân trang ổn định.
- Category CRUD.
- Product CRUD, search/filter/sort/paging.
- Upload ảnh local qua `IUploadService`, lưu trong `Uploads/products`.
- Mỗi sản phẩm có tối đa một ảnh chính; xóa ảnh chính sẽ tự chọn ảnh thay thế.
- Cart CRUD, gộp item trùng và hiển thị giá/tồn kho hiện tại.
- Checkout bắt buộc `Idempotency-Key`, khóa tồn kho chống oversell và rollback khi thất bại.
- Đơn mới ở `Pending`, giữ tồn kho khi checkout, lưu snapshot/status history và hoàn kho đúng một lần khi hủy.
- Payment state machine/history, public checkout capability endpoint, COD adapter và HMAC webhook giới hạn raw body, chống replay, lưu kết quả xử lý ổn định.
- Transactional outbox gửi thông báo qua SMTP cấu hình được, có retry và dead-letter.
- Inventory ledger, danh sách tồn kho thấp và lịch sử biến động theo sản phẩm.
- Sales summary theo UTC cho order/payment breakdown, tiền thu/hoàn/doanh thu thuần, top sản phẩm đã giao và tồn kho thấp có ngưỡng.
- Permission policy cho các endpoint quản trị; đổi password/role thu hồi phiên ngay.
- Rate limit cho auth, refresh token và upload.
- Validation bằng FluentValidation.
- AutoMapper profile cho User, Category, Product, Cart và Order response.
- Exception, validation, auth va rate-limit errors dung `application/problem+json`, co stable `code` va `traceId`.
- Serilog console + rolling file log.
- Swagger Bearer support.
- Swagger default error responses va success response schemas.
- `ECommerceBackend.http` request collection cho auth, product/category, cart va order.
- 133 unit tests va 10 SQL Server integration flows co database tam cho checkout, session, payment/webhook, reporting, migration va outbox.
- Health checks tai `/health/live`, `/health/ready`, `/health`.
- Production config validation cho JWT/CORS va deployment guide.
- Correlation ID va Serilog request logging de noi response `traceId` voi console/file log.

Tài liệu kỹ thuật:

- `docs/ARCHITECTURE.md`: module, transaction và luồng trạng thái.
- `docs/AUTHORIZATION.md`: ma trận role/permission và phạm vi endpoint.
- `docs/DATABASE.md`: quan hệ, constraint và chính sách migration.

Additional backend hardening:

- Money validation theo `decimal(18,2)`, tinh tong order co guard overflow, va paging on dinh cho product/order.
- Cart fallback creation xu ly race khi request dau tien tao cart song song.
- Reporting mac dinh dung UTC clock duoc inject, gioi han query co stable error code va duoc kiem thu boundary tren SQL Server.
- Outbox enqueue/retry/readiness dung cung injected UTC clock; client-aborted requests khong bi ghi thanh loi `500`.

## Final Quality Gate

- OpenAPI security metadata follows the authenticated fallback policy and keeps reviewed public endpoints anonymous.
- Payment webhook documents payload-limit and rate-limit responses.
- Release publish excludes local settings and configuration templates.
- CI runs release build, format verification, migration-model validation and NuGet vulnerability auditing.
- CI runs both the non-SQL suite and dedicated SQL Server integration suite.
- Coverage reports are uploaded by CI. The regression gate requires at least 75% line coverage and 60% branch coverage.

## Auth Token Notes

`POST /api/auth/register` và `POST /api/auth/login` trả về:

- `accessToken`: JWT dùng trong header `Authorization: Bearer {accessToken}`.
- `token`: alias giữ tương thích với response cũ.
- `accessTokenExpiresAt`: thời điểm access token hết hạn.
- `refreshToken`: token dùng để gọi `POST /api/auth/refresh`.
- `refreshTokenExpiresAt`: thời điểm refresh token hết hạn.

Refresh token được lưu dưới dạng hash và nhóm theo token family. Token cũ bị reuse sẽ thu hồi
toàn bộ family. Access token chỉ hợp lệ khi user version và session trong database còn hiệu lực.

## Verification

```bash
dotnet build --no-restore
dotnet format ECommerceBackend.sln --no-restore --verify-no-changes
dotnet test --no-restore
dotnet test --settings coverage.runsettings --collect:"XPlat Code Coverage"
```

Chạy riêng integration test SQL Server:

```powershell
$env:RUN_SQL_INTEGRATION_TESTS="1"
dotnet test --filter "Category=SqlServerIntegration"
```

## Deployment

Xem [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) de cau hinh secrets, CORS, health checks va migration flow cho production.
