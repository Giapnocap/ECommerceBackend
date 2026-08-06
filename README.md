# ECommerce Backend

REST API cho hệ thống thương mại điện tử, xây dựng bằng ASP.NET Core 8 và SQL Server. Dự án tập trung vào tính nhất quán dữ liệu, phân quyền, xử lý đồng thời và khả năng vận hành của các luồng backend thực tế.

Phạm vi repository là backend và database. Frontend, container và hạ tầng cloud không thuộc phạm
vi triển khai để giữ trọng tâm vào API, nghiệp vụ, persistence và độ tin cậy dữ liệu.

## Công Nghệ

- .NET 8, ASP.NET Core Web API
- Entity Framework Core, SQL Server
- JWT Bearer, role và permission policies
- FluentValidation và mapping response tường minh
- Serilog, ProblemDetails, correlation ID
- Swagger/OpenAPI
- xUnit unit tests, API/EF Core integration tests và SQL Server integration tests

## Chức Năng Chính

- Đăng ký, đăng nhập, refresh-token rotation, phát hiện token reuse và thu hồi phiên.
- Quản lý người dùng và phân vai trò `Admin`, `Staff`, `Customer`.
- CRUD danh mục, sản phẩm và ảnh sản phẩm.
- Tìm kiếm, lọc, sắp xếp và phân trang sản phẩm.
- Giỏ hàng và checkout có `Idempotency-Key`.
- Báo giá checkout phía server, phí giao hàng cấu hình được và promotion có giới hạn tổng/theo khách.
- Vận đơn một-một lưu đơn vị vận chuyển, mã theo dõi, thời điểm xuất giao và giao thành công.
- Quy trình trả hàng gồm Customer yêu cầu, Staff duyệt/từ chối, nhận kiểm hàng, hoàn kho và hoàn tiền.
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
  -> Repository / IUnitOfWork / External Adapter
  -> SQL Server / File Storage
```

```text
src/
  ECommerceBackend/
    API/              Controllers, middleware, composition root và Swagger
  ECommerceBackend.Application/
    Features/         Capability-owned DTO, validator, service, use case và repository contract
      Auth, Users, Catalog, Carts, Orders, Payments,
      Promotions, Inventory, Reports, Operations, Notifications/
    Common/           Paging, options và application primitives dùng chung
    Interfaces/       Chỉ chứa transaction, consistency, request context và persistence dùng chung
    Mappings/         Response mapping dùng qua nhiều feature
  ECommerceBackend.Domain/
    Entities/         Entities và aggregate state
    Enums/            Status và state transitions
    Policies/         Business policies
  ECommerceBackend.Infrastructure/
    Data/Repositories/ EF Core repository implementations
    Data/Configurations/ Entity mappings, indexes, constraints và seed data
    Migrations/       SQL Server migration history
    Notifications/    Outbox processing và notification adapters
    Payments/         Payment provider adapters
tests/
  ECommerceBackend.UnitTests/
    Application/     Validator và application rules không dùng Infrastructure
    Domain/          Aggregate, policy và state-machine tests
  ECommerceBackend.IntegrationTests/
    API/             HTTP contracts, middleware và OpenAPI baseline
    Auth, Catalog, Carts, Orders, Payments, Operations/
                     Service/repository workflows theo feature
    SqlServer/       Migration, transaction, recovery và performance tests
    Support/         Fixture và factory dùng chung
```

Solution gồm sáu project: API host `src/ECommerceBackend/ECommerceBackend.csproj`, `Domain`, `Application`,
`Infrastructure`, `ECommerceBackend.UnitTests` và `ECommerceBackend.IntegrationTests`. Khi chạy bằng Visual Studio, đặt project
`API` trong solution folder `src` làm Startup Project; các project layer là class library và không chạy
độc lập.

`src/ECommerceBackend/Program.cs` là composition root. Controller không chứa transaction logic; Application điều phối use case và transaction qua repository cùng `IUnitOfWork`; Domain bảo vệ invariant; Infrastructure triển khai EF Core persistence, locking và external adapters.

Source trong Application được nhóm theo capability để một luồng nghiệp vụ nằm gần các DTO,
validator, facade, use case và repository contract liên quan. Namespace hiện tại được giữ ổn định;
việc tổ chức lại thư mục không làm thay đổi API hoặc dependency direction giữa các project.

Đăng ký dependency thuộc về
`ECommerceBackend.Application/DependencyInjection.cs` và
`ECommerceBackend.Infrastructure/DependencyInjection.cs`; API chỉ ghép các module tại composition root.
`AppDbContext` chỉ khai báo `DbSet` và nạp
`IEntityTypeConfiguration<T>` từ `src/ECommerceBackend.Infrastructure/Data/Configurations`; mapping không nằm trong
application service. Repository là các contract theo feature, không sử dụng generic repository và
không expose `DbSet` hoặc `IQueryable` qua tầng Application.

Tài liệu thiết kế:

- [Kiến trúc và business invariants](docs/ARCHITECTURE.md)
- [Database ERD](docs/ERD.md)
- [Sequence các luồng quan trọng](docs/SEQUENCES.md)
- [Hiệu năng và quyết định scale](docs/PERFORMANCE.md)
- [Giới hạn hệ thống](docs/LIMITATIONS.md)

## Phân Quyền

| Vai trò | Phạm vi chính |
|---|---|
| `Customer` | Giỏ hàng, checkout, xem/hủy đơn thuộc sở hữu và yêu cầu trả hàng |
| `Staff` | Xác nhận đơn, vận đơn, giao hàng, xét duyệt/nhận hàng hoàn và xem tồn kho |
| `Admin` | Toàn bộ quyền Staff, quản lý catalog/user, báo cáo và operations |

Các endpoint quản trị sử dụng permission policies. Riêng operations recovery và audit yêu cầu role `Admin`.

## Phiên Bản API

- Route khuyến nghị: `/api/v1/...`.
- Route cũ `/api/...` vẫn hoạt động với phiên bản mặc định `1.0` để không phá client hiện tại.
- Response của endpoint hợp lệ có header `api-supported-versions`.
- Swagger/OpenAPI v1: `/swagger/v1/swagger.json` và `/swagger`.
- Thông báo và `ProblemDetails` dùng tiếng Việt; trường `code` giữ tiếng Anh ổn định cho client.

## Bằng Chứng Chất Lượng

| Hạng mục | Cơ chế kiểm chứng |
|---|---|
| Chất lượng build | Nullable reference types và warning-as-error cho toàn solution |
| Biên kiến trúc | Test chặn dependency sai chiều và EF Core rò rỉ vào Application |
| Hợp đồng API | OpenAPI v1 snapshot test, kiểm tra route cũ và `/api/v1` |
| Tính đúng dữ liệu | SQL Server integration test cho transaction, lock, idempotency và concurrency |
| Migration | Kiểm tra model drift, script nâng cấp/rollback, backup và restore drill |
| Coverage | CI chặn khi line coverage dưới 80% hoặc branch coverage dưới 60% |
| Hiệu năng | Budget SQL Server cho catalog, dữ liệu nhiều ảnh, lịch sử đơn, session và checkout 50 dòng |
| Release | ZIP có manifest, SHA-256, migration artifact và smoke test health endpoint |

Chi tiết và giới hạn của các kết quả đo được ghi tại
[Hiệu năng và quyết định scale](docs/PERFORMANCE.md) cùng
[Giới hạn hệ thống](docs/LIMITATIONS.md); các con số không được trình bày như năng lực production.

## Chạy Local

Yêu cầu:

- .NET SDK 8.x
- SQL Server
- Visual Studio 2022 hoặc .NET CLI

Repository có `global.json` để chọn SDK .NET 8 mới nhất đang cài trên máy.

Tạo cấu hình local:

```powershell
Copy-Item src/ECommerceBackend/appsettings.Local.example.json src/ECommerceBackend/appsettings.Local.json
```

Đặt JWT key riêng tối thiểu 32 bytes trong `src/ECommerceBackend/appsettings.Local.json`. Có thể thay connection string mặc định bằng:

```powershell
$env:ConnectionStrings__Default = "Server=.;Database=ECommerceDB;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;"
```

Khởi tạo và chạy:

```powershell
dotnet restore ECommerceBackend.sln
dotnet ef database update `
  --project src/ECommerceBackend.Infrastructure/ECommerceBackend.Infrastructure.csproj `
  --startup-project src/ECommerceBackend/ECommerceBackend.csproj
dotnet run --project src/ECommerceBackend/ECommerceBackend.csproj
```

- API: `http://localhost:5171`
- Swagger: `http://localhost:5171/swagger`
- Health: `http://localhost:5171/health/ready`

`/health/live` chỉ xác nhận tiến trình API còn hoạt động. `/health/ready` kiểm tra kết nối
database, khả năng ghi kho ảnh sản phẩm và trạng thái các tiến trình outbox, hết hạn đơn, lưu giữ
dữ liệu. Mỗi dependency check có timeout mặc định 5 giây qua
`HealthChecks:DependencyTimeoutSeconds`; giá trị hợp lệ từ 1 đến 30 giây. Chi tiết từng check chỉ
được trả tại `/health/details` cho tài khoản `Admin`.

### Tạo Admin Đầu Tiên

Trong `src/ECommerceBackend/appsettings.Local.json`, cấu hình thông tin riêng và đặt `AdminBootstrap:Enabled` thành `true`. Chạy ứng dụng một lần, sau đó tắt lại tùy chọn này. Không commit password hoặc JWT key.

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
Demo seed còn tạo mã `WELCOME10`: giảm 10%, tối đa 100.000 đ, cho đơn từ
500.000 đ và mỗi khách sử dụng một lần.

### Kịch Bản Demo End-to-End

1. Admin tạo danh mục, sản phẩm và tải ảnh qua `/api/v1/categories` và `/api/v1/products`.
2. Customer đăng nhập, thêm sản phẩm vào giỏ và gọi `/api/v1/orders/quote`.
3. Customer đặt hàng với `Idempotency-Key`; gửi lại cùng request nhận đúng đơn cũ.
4. Staff xác nhận đơn, tạo vận đơn, xuất giao và ghi nhận giao thành công.
5. Customer gửi yêu cầu trả hàng trong thời hạn cấu hình.
6. Staff duyệt, nhận kiểm hàng; hệ thống hoàn tồn kho và ghi inventory ledger trong transaction.
7. Staff ghi nhận hoàn tiền COD; payment history và order history được cập nhật đồng bộ.
8. Admin kiểm tra báo cáo, audit trail, outbox lỗi và lịch sử tồn kho.

Các request theo đúng thứ tự này có trong
[ECommerceBackend.http](src/ECommerceBackend/ECommerceBackend.http). Dùng route `/api/v1`; route
`/api` chỉ được giữ để kiểm tra tương thích ngược.

## Kiểm Thử API

- Swagger hỗ trợ Bearer authentication và mô tả success/error contracts.
- `POST /api/v1/orders/quote` tính giá hiện tại; `POST /api/v1/orders` luôn tính lại trong transaction.
- Admin quản lý promotion qua `/api/v1/promotions` bằng quyền quản lý sản phẩm.
- Staff/Admin xuất giao qua `/shipment/dispatch`, xác nhận giao qua `/shipment/deliver`.
- Customer tạo `/return-request`; Staff/Admin xét duyệt, nhận hàng hoàn rồi mới ghi nhận refund.
- [ECommerceBackend.http](src/ECommerceBackend/ECommerceBackend.http) chứa request mẫu cho auth, catalog, cart và order.
- API error sử dụng `application/problem+json`, có `code` và `traceId` ổn định.

## Chạy Test

Unit tests thuần Domain/Application:

```powershell
dotnet test tests/ECommerceBackend.UnitTests/ECommerceBackend.UnitTests.csproj
```

API, repository và application integration tests không yêu cầu SQL Server:

```powershell
dotnet test tests/ECommerceBackend.IntegrationTests/ECommerceBackend.IntegrationTests.csproj `
  --filter "Category!=SqlServerIntegration&Category!=SqlServerRecoveryIntegration&Category!=SqlServerPerformance"
```

SQL Server integration tests dùng database riêng:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\BuildMigrationArtifacts.ps1 -OutputDirectory .\MigrationArtifacts
$env:RUN_SQL_INTEGRATION_TESTS = "1"
$env:ECOMMERCE_TEST_SQL_CONNECTION = "Server=.;Database=ECommerceBackendIntegration;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;"
$env:ECOMMERCE_MIGRATION_ARTIFACTS_DIRECTORY = (Resolve-Path .\MigrationArtifacts)
dotnet test tests/ECommerceBackend.IntegrationTests/ECommerceBackend.IntegrationTests.csproj `
  --filter "Category=SqlServerIntegration"
```

Các test này tạo và xóa database tạm. `ECOMMERCE_TEST_SQL_CONNECTION` phải trỏ tới database
riêng có tên chứa `Integration`; không dùng connection string của database ứng dụng.
Chạy `scripts/BuildMigrationArtifacts.ps1` trước khi test để tạo thư mục artifact dùng cho
kiểm tra nâng cấp, rollback và nâng cấp lại migration.

Performance tests dùng SQL Server thật, tạo database tạm và đo catalog thường/từ khóa/nhiều ảnh,
lịch sử đơn hàng, session validation và checkout 50 dòng sau bước warm-up:

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

Migration fulfillment mới chặn rollback sau khi đã có shipment, return request hoặc trạng thái
`ReturnRequested`/`ReturnApproved`/`Refunded`. Khi đã phát sinh dữ liệu, phục hồi bản backup trước
migration thay vì xóa lịch sử vận đơn và trả hàng.

Migration snapshot người nhận cũng chặn rollback khi bảng `Orders` đã có dữ liệu, vì việc xóa
`RecipientName` và `RecipientPhone` sẽ làm mất lịch sử giao hàng. Trong trường hợp này phải phục
hồi bản backup trước migration thay vì dùng `rollback-last.sql`.

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

Workflow `Backend Release` có thể chạy thủ công để kiểm tra trước, hoặc tự chạy khi push tag dạng
`v*`; package đã xác minh được lưu dưới dạng GitHub Actions artifact.

## Điểm Kỹ Thuật Nổi Bật

- Checkout khóa cart và product theo thứ tự ổn định trong một transaction.
- Idempotency ngăn tạo trùng order khi client retry.
- Row version, unique constraints và SQL locks bảo vệ race condition.
- Order lưu snapshot người nhận; order detail lưu snapshot tên và giá để giữ lịch sử chính xác.
- Payment webhook xác thực chữ ký, event ID và payload trước khi thay đổi trạng thái.
- Outbox ghi cùng transaction với business data, xử lý retry/dead-letter ở background và giữ
  nguyên `Message-ID` qua các lần gửi lại.
- Mọi thay đổi tồn kho đều sinh ledger entry có balance sau giao dịch.
- Correlation ID liên kết ProblemDetails, request log và audit event.

## Bảo Mật Cấu Hình

- `src/ECommerceBackend/appsettings.Local.json`, logs, uploads và data-protection keys không được commit.
- `src/ECommerceBackend/appsettings.Production.example.json` chỉ là template, không chứa secret thật.
- Production validation từ chối JWT key yếu, CORS không hợp lệ và cấu hình webhook thiếu an toàn.
- Khi `OrderLifecycle:RequireExpirationProcessing=true`, worker hết hạn đơn phải được bật và không được chạy dry-run.
