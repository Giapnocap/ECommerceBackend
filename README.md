# ECommerce Backend

REST API cho hệ thống thương mại điện tử, xây dựng bằng ASP.NET Core 8 và SQL Server. Dự án tập trung vào tính nhất quán dữ liệu, phân quyền, xử lý đồng thời và khả năng vận hành của các luồng backend thực tế.

## Công Nghệ

- .NET 8, ASP.NET Core Web API
- Entity Framework Core, SQL Server
- JWT Bearer, role và permission policies
- FluentValidation và mapping response tường minh
- Serilog, ProblemDetails, correlation ID
- Swagger/OpenAPI
- xUnit, EF Core InMemory và SQL Server integration tests

## Chức Năng Chính

- Đăng ký, đăng nhập, refresh-token rotation, phát hiện token reuse và thu hồi phiên.
- Quản lý người dùng và phân vai trò `Admin`, `Staff`, `Customer`.
- CRUD danh mục, sản phẩm và ảnh sản phẩm.
- Tìm kiếm, lọc, sắp xếp và phân trang sản phẩm.
- Giỏ hàng và checkout có `Idempotency-Key`.
- Vòng đời đơn hàng gồm giao lại, giao thất bại, hoàn hàng và tự động hết hạn đơn `Pending`.
- Giữ/hoàn tồn kho có inventory ledger riêng cho hủy đơn và nhận hàng hoàn.
- Payment state machine, COD, ghi nhận hoàn tiền thủ công và HMAC webhook chống replay.
- Transactional outbox, retry, dead-letter và redrive.
- Báo cáo doanh thu, trạng thái đơn, sản phẩm bán chạy và tồn kho thấp.
- Audit trail cho các thao tác đặc quyền và đối soát file upload.

## Kiến Trúc

```text
API request
  -> Middleware / Authorization / Validation
  -> Controller
  -> Application Service
  -> Domain Entity / Policy
  -> IAppDbContext / External Adapter
  -> SQL Server / File Storage
```

```text
API/                 Controllers, middleware, health checks, Swagger, DI
Application/         DTOs, validators, interfaces, use-case services
Domain/              Entities, enums, state transitions, business policies
Infrastructure/      EF Core, SQL Server, migrations, background services
ECommerceBackend.Tests/  Unit, contract và SQL Server integration tests
```

`Program.cs` là composition root. Controller không chứa transaction logic; Application điều phối use case; Domain bảo vệ invariant; Infrastructure triển khai persistence, locking và external adapters.

Tài liệu thiết kế:

- [Kiến trúc và business invariants](docs/ARCHITECTURE.md)
- [Database ERD](docs/ERD.md)
- [Sequence các luồng quan trọng](docs/SEQUENCES.md)
- [Hiệu năng và quyết định scale](docs/PERFORMANCE.md)
- [Giới hạn hệ thống](docs/LIMITATIONS.md)

## Phân Quyền

| Vai trò | Phạm vi chính |
|---|---|
| `Customer` | Giỏ hàng, checkout, xem và hủy đơn thuộc sở hữu |
| `Staff` | Xử lý đơn hàng, xem tồn kho và inventory ledger |
| `Admin` | Toàn bộ quyền Staff, quản lý catalog/user, báo cáo và operations |

Các endpoint quản trị sử dụng permission policies. Riêng operations recovery và audit yêu cầu role `Admin`.

## Chạy Local

Yêu cầu:

- .NET SDK 8.x
- SQL Server
- Visual Studio 2022 hoặc .NET CLI

Tạo cấu hình local:

```powershell
Copy-Item appsettings.Local.example.json appsettings.Local.json
```

Đặt JWT key riêng tối thiểu 32 bytes trong `appsettings.Local.json`. Có thể thay connection string mặc định bằng:

```powershell
$env:ConnectionStrings__Default = "Server=.;Database=ECommerceDB;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;"
```

Khởi tạo và chạy:

```powershell
dotnet restore
dotnet ef database update
dotnet run
```

- API: `http://localhost:5171`
- Swagger: `http://localhost:5171/swagger`
- Health: `http://localhost:5171/health/ready`

### Tạo Admin Đầu Tiên

Trong `appsettings.Local.json`, cấu hình thông tin riêng và đặt `AdminBootstrap:Enabled` thành `true`. Chạy ứng dụng một lần, sau đó tắt lại tùy chọn này. Không commit password hoặc JWT key.

### Dữ Liệu Demo

Sau khi áp dụng migration và tạo Admin, có thể seed dữ liệu local để chạy thử toàn bộ luồng web:

```powershell
sqlcmd -S . -d ECommerceDB -E -C -f 65001 -v EnvironmentName=Development -i scripts/SeedDemoData.sql
```

Biến `EnvironmentName` là bắt buộc. Script chỉ chấp nhận `Development`, `Local`
hoặc `Testing`, đồng thời từ chối database có tên chứa `Prod`/`Production`.
Script có thể chạy lại an toàn và không ghi đè dữ liệu đã phát sinh:

| Vai trò | Username | Password |
|---|---|---|
| `Staff` | `demo.staff` | `Staff@ECommerce2026!` |
| `Customer` | `demo.customer` | `Customer@ECommerce2026!` |

Các tài khoản và mật khẩu trên chỉ dùng cho database development local.

## Kiểm Thử API

- Swagger hỗ trợ Bearer authentication và mô tả success/error contracts.
- [ECommerceBackend.http](ECommerceBackend.http) chứa request mẫu cho auth, catalog, cart và order.
- API error sử dụng `application/problem+json`, có `code` và `traceId` ổn định.

## Chạy Test

Unit và application tests:

```powershell
dotnet test ECommerceBackend.sln --filter "Category!=SqlServerIntegration&Category!=SqlServerRecoveryIntegration&Category!=SqlServerPerformance"
```

SQL Server integration tests dùng database riêng:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\BuildMigrationArtifacts.ps1 -OutputDirectory .\MigrationArtifacts
$env:RUN_SQL_INTEGRATION_TESTS = "1"
$env:ECOMMERCE_TEST_SQL_CONNECTION = "Server=.;Database=ECommerceBackendIntegration;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;"
$env:ECOMMERCE_MIGRATION_ARTIFACTS_DIRECTORY = (Resolve-Path .\MigrationArtifacts)
dotnet test ECommerceBackend.Tests/ECommerceBackend.Tests.csproj --filter "Category=SqlServerIntegration"
```

Các test này tạo và xóa database tạm. `ECOMMERCE_TEST_SQL_CONNECTION` phải trỏ tới database
riêng có tên chứa `Integration`; không dùng connection string của database ứng dụng.
Chạy `scripts/BuildMigrationArtifacts.ps1` trước khi test để tạo thư mục artifact dùng cho
kiểm tra nâng cấp, rollback và nâng cấp lại migration.

Performance tests dùng SQL Server thật, tạo database tạm và đo catalog, session validation,
checkout sau bước warm-up:

```powershell
.\scripts\RunPerformanceTests.ps1 `
  -ConnectionString "Server=.;Database=ECommerceBackendPerformance;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;" `
  -ResultsDirectory .\PerformanceResults `
  -Configuration Release
```

Connection string phải trỏ tới database riêng có tên chứa `Performance` hoặc `Integration`.
Kết quả được ghi tại `PerformanceResults/performance-results.json`. Workflow
`Backend Performance` chạy hằng tuần hoặc thủ công, không làm chậm pull request thông thường.

CI còn chạy recovery drill với thư mục backup nằm trong SQL Server container. Khi chạy thủ công,
đặt `ECOMMERCE_TEST_SQL_BACKUP_DIRECTORY` thành thư mục mà tài khoản dịch vụ SQL Server có quyền
ghi, sau đó dùng filter `Category=SqlServerRecoveryIntegration`. Recovery drill tạo database
riêng, kiểm tra checksum backup, thay đổi schema/dữ liệu, restore và xác minh dữ liệu đã phục hồi.

Trước khi nâng cấp production: kiểm tra checksum artifact, tạo full backup, restore thử vào
SQL Server cô lập, áp dụng `migrate-up.sql` trong maintenance window và chạy smoke test. Chỉ dùng
`rollback-last.sql` khi thay đổi tương thích với dữ liệu cũ; nếu migration đã làm mất dữ liệu,
phục hồi từ bản backup đã được kiểm chứng.

Migration vòng đời hoàn hàng chỉ rollback được khi chưa có order status `DeliveryFailed`/`Returned`,
inventory movement `OrderReturned`, manual refund history hoặc nhiều lần đi qua cùng trạng thái.
Nếu đã phát sinh các dữ liệu này, giữ migration hiện tại hoặc phục hồi backup thay vì ép chạy script
rollback.

CI thực hiện restore có vulnerability audit, format check, Release build, kiểm tra model/migration,
coverage gate và SQL Server integration tests. Baseline và quyết định scale được ghi tại
[`docs/PERFORMANCE.md`](docs/PERFORMANCE.md).

## Đóng Gói Release

Sau khi Release build và sinh migration artifact:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\BuildReleasePackage.ps1 `
  -OutputDirectory .\ReleasePackage `
  -MigrationArtifactsDirectory .\MigrationArtifacts `
  -SourceRevision local-working-tree
```

Output gồm ZIP triển khai backend, checksum SHA-256 và manifest liệt kê checksum từng file.
Package chứa migration forward/rollback nhưng không chứa cấu hình Development, local hoặc template
production. Secret production phải được cấp qua environment variable hoặc secret store lúc deploy.

Xác minh checksum, nội dung manifest và khởi động DLL đã publish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\VerifyReleasePackage.ps1 `
  -ReleaseDirectory .\ReleasePackage `
  -SmokeTest
```

Push tag dạng `v*` sẽ chạy workflow `Backend Release` và lưu package đã xác minh dưới dạng
GitHub Actions artifact.

## Điểm Kỹ Thuật Nổi Bật

- Checkout khóa cart và product theo thứ tự ổn định trong một transaction.
- Idempotency ngăn tạo trùng order khi client retry.
- Row version, unique constraints và SQL locks bảo vệ race condition.
- Order detail lưu snapshot tên và giá để giữ lịch sử chính xác.
- Payment webhook xác thực chữ ký, event ID và payload trước khi thay đổi trạng thái.
- Outbox ghi cùng transaction với business data, xử lý retry/dead-letter ở background và giữ
  nguyên `Message-ID` qua các lần gửi lại.
- Mọi thay đổi tồn kho đều sinh ledger entry có balance sau giao dịch.
- Correlation ID liên kết ProblemDetails, request log và audit event.

## Bảo Mật Cấu Hình

- `appsettings.Local.json`, logs, uploads và data-protection keys không được commit.
- `appsettings.Production.example.json` chỉ là template, không chứa secret thật.
- Production validation từ chối JWT key yếu, CORS không hợp lệ và cấu hình webhook thiếu an toàn.
- Khi `OrderLifecycle:RequireExpirationProcessing=true`, worker hết hạn đơn phải được bật và không được chạy dry-run.
