# Báo Cáo Hoàn Thiện Backend

Ngày xác minh cuối: `09/08/2026`

Trạng thái: toàn bộ gate trong phạm vi repository đã đạt. Docker Compose đã được build/up thật trên
máy local; commit code `0ae568f` đã đạt cả ba job của Backend CI trên GitHub Actions.

## 1. Root Cause Analysis

Workflow trên GitHub ở commit `60c7aa8` thất bại trong nhóm kiểm tra migration. Việc tái hiện local
cho thấy ba nguyên nhân liên quan:

1. Lệnh EF dùng project API làm startup project. Khi EF tạo `DbContext` ở design time, toàn bộ cấu
   hình API bị khởi tạo và dừng vì thiếu JWT secret dành cho runtime.
2. Service graph của API không cung cấp một đường tạo `DbContextOptions<AppDbContext>` độc lập,
   ổn định cho migration tooling.
3. Migration lịch sử `20260717160641_CompleteBackendFoundation` đã bị sửa sau khi được coi là đã
   phát hành. Điều này làm mất tính bất biến của lịch sử migration và khiến fresh database có thể
   khác database đã nâng cấp trước đó.

Lệnh thất bại chính:

```powershell
dotnet ef migrations has-pending-model-changes `
  --project src/ECommerceBackend.Infrastructure/ECommerceBackend.Infrastructure.csproj `
  --startup-project src/ECommerceBackend/ECommerceBackend.csproj
```

Đây là lỗi thiết kế luồng migration/tooling, không phải lý do để bỏ drift check hoặc nới lỏng CI.

## 2. Migration Fixes

- Khôi phục nội dung đúng của migration lịch sử
  `20260717160641_CompleteBackendFoundation`; không tiếp tục sửa migration đã công bố.
- Tạo migration forward-only `20260807132144_InvalidateLegacyAdminCredential` để vô hiệu hóa đúng
  credential admin cũ bằng điều kiện hash. `Down` không phục hồi credential đã bị vô hiệu hóa.
- Thêm `AppDbContextDesignTimeFactory` trong Infrastructure để EF tooling chỉ cần cấu hình database,
  không phụ thuộc JWT, SMTP hoặc runtime service graph của API.
- Dùng Infrastructure làm cả migrations project và startup project trong CI, Docker migration stage
  và `BuildMigrationArtifacts.ps1`.
- Khóa `dotnet-ef` ở phiên bản `8.0.20` và cho script nhận đường dẫn tool rõ ràng.
- Bổ sung test SQL Server cho fresh migration, upgrade, rollback, re-upgrade và checksum artifact.

Kết quả hội tụ đã kiểm tra:

```text
Fresh database -> toàn bộ 24 migrations -> schema hiện tại
Existing database -> migration mới -> cùng schema hiện tại
```

`migrations has-pending-model-changes` không còn phát hiện model drift.

## 3. Docker

Môi trường local gồm ba service:

```text
sqlserver (SQL Server 2022, persistent volume, health check)
    -> migrate (one-shot EF migration container)
        -> api (.NET 8 ASP.NET Runtime, non-root user)
```

`Dockerfile` dùng các stage `restore`, `build`, `publish`, `migrations` và `runtime`. SDK/tool EF không
nằm trong runtime image. API lắng nghe cổng container `8080`; Compose mặc định chỉ bind host
`127.0.0.1:5171`.

`docker-compose.yml` dùng hostname `sqlserver`, chờ health check và chỉ chạy API sau khi migration
container hoàn thành. SQL data, ảnh sản phẩm, data-protection keys và log dùng named volume.
Ứng dụng không gọi `Database.Migrate()` trong startup production.

Cách chạy dự kiến:

```powershell
Copy-Item .env.example .env
# Thay MSSQL_SA_PASSWORD và JWT_KEY bằng secret local.
docker compose config --quiet
docker compose up --build
```

Trạng thái xác minh local ngày `09/08/2026`:

```text
docker compose up --build --detach -> thành công
sqlserver                         -> running, healthy
migrate                           -> exited, exit code 0
GET /health/live                  -> HTTP 200, Healthy
GET /health/ready                 -> HTTP 200, Healthy
```

Kết quả này xác nhận image API/migration build được, SQL Server khởi động, migration one-shot hoàn
thành và API truy cập được dependency thật. Workflow CI vẫn có job độc lập để lặp lại Docker smoke,
thu log và dọn volume trên Ubuntu runner.

## 4. Exception Changes

- Bỏ blanket mapping mọi `ArgumentException` thành HTTP 400. `ArgumentException` không được phân
  loại trước giờ trả về 500 an toàn vì có thể là lỗi lập trình phía server.
- `DomainRuleViolationException` trả HTTP 422 với stable domain error code.
- Chỉ `ApiException` có status 4xx hợp lệ mới được trả nguyên contract; status ngoài khoảng client
  được chuẩn hóa thành generic 500.
- Response 500 không chứa exception message, stack trace hoặc implementation detail, kể cả ở
  Development.
- Structured server log, stack trace trong log và correlation ID vẫn được giữ nguyên.
- ProblemDetails tiếp tục dùng contract nhất quán cho validation, application error và unexpected
  error; Domain không phụ thuộc HTTP.

Regression tests đã xác minh mapping 422, generic 500, không rò chi tiết và giữ correlation ID.

## 5. Domain Changes

Các entity trọng yếu được giới hạn việc gán state trực tiếp nhưng vẫn tương thích EF Core:

- `Order`: định danh, idempotency, snapshot, pricing và trạng thái khởi tạo đi qua `Create` cùng các
  domain method hiện có.
- `OrderDetail`: snapshot tên/giá, số lượng và định danh đi qua `Create`.
- `Payment`: trạng thái khởi tạo, amount, provider và transaction ID đi qua `Create`/transition.
- `Product`: tạo/cập nhật và thay đổi tồn kho tiếp tục đi qua domain methods/policy.
- `RefreshToken`: identity, family, hash và thời hạn đi qua `Create`; rotate/revoke/reuse vẫn qua
  domain methods.
- `InventoryTransaction`: tạo ledger entry qua `Create`, kiểm tra chiều biến động, loại giao dịch,
  order reference, số dư và độ dài reason.
- `Shipment` và `ReturnRequest`: business state được đóng gói trong factory/transition phù hợp.

Các application workflow tương ứng được chuyển từ object initializer sang factory: phát hành refresh
token, tạo sản phẩm/tồn kho đầu kỳ, checkout, hủy đơn và nhận hàng hoàn. API contract, schema và thứ
tự transaction không thay đổi. Navigation properties cần cho EF vẫn được giữ theo mapping hiện tại.

Không thêm Value Object, Domain Event, generic repository hoặc abstraction mới khi không giải quyết
lỗi thực tế.

## 6. Regression Verification

### Checkout

- Duplicate request cùng idempotency key trả cùng một order đã commit.
- Cùng key nhưng payload khác bị từ chối.
- Hai khách cạnh tranh sản phẩm còn đúng một đơn vị chỉ có một checkout thành công.
- Hai request checkout đồng thời của cùng khách không tạo hai order.
- Giá và tồn kho được đọc phía server; client không quyết định đơn giá.
- Order, payment, inventory ledger, cart mutation và outbox nằm trong cùng transaction.
- Trigger gây lỗi khi insert outbox đã chứng minh toàn bộ business mutation rollback.
- Pessimistic lock, RowVersion và unique constraint hiện có được giữ nguyên.

### Authentication

- Normal refresh tạo token mới và revoke token cũ.
- Reuse token cũ revoke đúng compromised device family, không làm mất session thiết bị khác.
- Expired/revoked token bị từ chối mà không mutate session.
- Concurrent refresh chỉ một nhánh hợp lệ.
- Logout một session và logout-all được kiểm tra riêng.
- Raw JWT/refresh token không được ghi log; refresh token lưu dưới dạng hash.

### Transactional Outbox

- Business data và outbox message commit trong cùng transaction.
- Worker có lease, retry/backoff, dead-letter và redrive.
- Test bao phủ retry, duplicate delivery, restart recovery, cancellation và outbox insert rollback.
- Không thay outbox SQL bằng in-memory queue và không thêm broker khi chưa có yêu cầu vận hành.

## 7. Tests

Các lệnh chính đã chạy:

```powershell
dotnet restore ECommerceBackend.sln `
  -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low `
  -warnaserror:NU1901,NU1902,NU1903,NU1904

dotnet format ECommerceBackend.sln --no-restore --verify-no-changes
dotnet build ECommerceBackend.sln --configuration Release --no-restore

.\.tools\dotnet-ef.exe migrations has-pending-model-changes `
  --project src/ECommerceBackend.Infrastructure/ECommerceBackend.Infrastructure.csproj `
  --startup-project src/ECommerceBackend.Infrastructure/ECommerceBackend.Infrastructure.csproj `
  --configuration Release --no-build

.\scripts\BuildMigrationArtifacts.ps1 `
  -OutputDirectory .\MigrationArtifacts `
  -DotNetEfPath .\.tools\dotnet-ef.exe

dotnet test tests/ECommerceBackend.UnitTests/ECommerceBackend.UnitTests.csproj `
  --configuration Release --no-build

dotnet test tests/ECommerceBackend.IntegrationTests/ECommerceBackend.IntegrationTests.csproj `
  --configuration Release --no-build `
  --filter "Category!=SqlServerIntegration&Category!=SqlServerRecoveryIntegration&Category!=SqlServerPerformance"

$env:RUN_SQL_INTEGRATION_TESTS = "1"
$env:ECOMMERCE_TEST_SQL_CONNECTION = "<isolated SQL Server test database>"
$env:ECOMMERCE_MIGRATION_ARTIFACTS_DIRECTORY = (Resolve-Path .\MigrationArtifacts)
dotnet test ECommerceBackend.sln --configuration Release --no-build

.\scripts\VerifyCoverage.ps1 `
  -ReportDirectory .\TestResults\CompletionV3FinalCoverage `
  -MinimumLineRate 80 -MinimumBranchRate 60

.\scripts\TestRepositorySecrets.ps1
.\scripts\BuildReleasePackage.ps1 `
  -OutputDirectory .\ReleasePackage\CompletionV3Final `
  -MigrationArtifactsDirectory .\MigrationArtifacts
.\scripts\VerifyReleasePackage.ps1 `
  -ReleaseDirectory .\ReleasePackage\CompletionV3Final -SmokeTest
```

Kết quả cuối:

| Gate | Kết quả |
|---|---:|
| NuGet vulnerability audit | Đạt |
| Format | Đạt |
| Release build | Đạt, 0 lỗi và 0 cảnh báo |
| Unit tests | 227/227 đạt |
| API/EF integration không cần SQL thật | 278/278 đạt |
| SQL Server integration | 23/23 đạt |
| SQL Server recovery | 1/1 đạt |
| SQL Server performance | 1/1 đạt |
| Full solution | 530/530 đạt, không skip |
| Line coverage | 83,31%, gate 80% |
| Branch coverage | 67,04%, gate 60% |
| EF model drift | Không phát hiện |
| Fresh/upgrade/rollback/re-upgrade migration | Đạt |
| Secret scan | Đạt |
| Release checksum/manifest/startup smoke | Đạt |

API/SQL smoke trên database tạm đã đạt cho readiness, Swagger, đăng ký, JWT, đọc sản phẩm, giỏ hàng,
checkout và idempotent replay. Database tạm được xóa sau test.

Performance gate cuối đạt các budget đã định nghĩa. Script tạo lại kết quả chi tiết tại
`PerformanceResults/performance-results.json`; thư mục artifact này được Git ignore và không phải cam
kết tải production.

## 8. CI

Workflow gồm ba nhóm độc lập:

1. Docker Compose smoke.
2. Build, format, audit, migration drift/artifact, unit/integration coverage và release package.
3. SQL Server integration/recovery tests.

Các action đã được nâng lên major hiện hành: `actions/checkout@v7`, `actions/setup-dotnet@v6` và
`actions/upload-artifact@v7`, loại bỏ dependency Node.js 20 đã bị cảnh báo trên run cũ.

Commit code `0ae568fbe14d77d403a65de44914d97aa6796d29` đã đạt **CI GREEN** tại
[GitHub Actions run 31299357392](https://github.com/Giapnocap/ECommerceBackend/actions/runs/31299357392).
Cả ba job `docker-compose-smoke`, `build-and-unit-tests` và `sql-server-integration-tests` đều hoàn
thành với kết quả `success`.

## 9. Remaining Limitations

- Thanh toán thực tế chưa tích hợp payment provider; luồng hiện tại tập trung COD và signed webhook
  Development/Testing.
- Ảnh sản phẩm nằm trên local/volume, chưa phù hợp nhiều API replica nếu không dùng shared/object
  storage.
- Rate limit chạy trong process; topology hỗ trợ hiện tại là một API instance và một SQL Server.
- Session validation đọc SQL Server trên protected request; chưa có bằng chứng tải buộc thêm cache.
- SMTP mang ngữ nghĩa at-least-once nên crash ở thời điểm đặc biệt vẫn có khả năng gửi email trùng.
- Chưa có cloud target, managed secrets, TLS termination, DNS hoặc centralized observability.
- Performance gate là regression baseline local/CI, không phải production SLA.
- Không thêm Redis, RabbitMQ, Kafka, Kubernetes, CQRS hay microservices vì chưa có bài toán thực tế
  biện minh cho chi phí đó.

## 10. Interview Preparation

Chủ project cần tự giải thích được ít nhất các câu hỏi sau:

1. Dependency direction giữa API, Application, Domain và Infrastructure được tổ chức ra sao?
2. Architecture tests đang ngăn những dependency sai chiều nào?
3. Vì sao dự án không dùng generic repository cho mọi entity?
4. Vì sao migration không chạy tự động trong `Program.cs`?
5. Design-time `DbContext` factory giải quyết lỗi CI nào và khác runtime DI như thế nào?
6. Vì sao không được sửa migration lịch sử đã phát hành?
7. Fresh database và existing database được chứng minh hội tụ schema bằng cách nào?
8. Tại sao migration vô hiệu hóa credential cũ không phục hồi password trong `Down`?
9. Checkout transaction bắt đầu/kết thúc ở đâu và những ghi dữ liệu nào nằm trong đó?
10. Pessimistic lock và optimistic concurrency đang giải quyết hai rủi ro khác nhau thế nào?
11. Idempotency key, request hash và unique constraint phối hợp ra sao?
12. Điều gì xảy ra khi hai khách cùng mua sản phẩm còn một đơn vị?
13. Vì sao server-side pricing là một invariant bảo mật, không chỉ là validation?
14. Inventory ledger khác việc chỉ cập nhật `StockQuantity` ở điểm nào?
15. Làm sao chứng minh lỗi ghi outbox không để lại order hoặc trừ tồn kho dở dang?
16. Transactional Outbox giải quyết failure window nào?
17. Vì sao outbox vẫn cần consumer idempotency dù message được lưu cùng transaction?
18. Lease, retry/backoff, dead-letter và redrive của outbox hoạt động ra sao?
19. Refresh-token rotation và reuse detection bảo vệ tài khoản như thế nào?
20. Vì sao reuse ở một thiết bị không nên revoke session hợp lệ của thiết bị khác?
21. Raw refresh token được bảo vệ trong database và log như thế nào?
22. Dự án phân biệt validation error, application error, domain violation và unexpected error ra sao?
23. Vì sao không ánh xạ mọi `ArgumentException` thành HTTP 400?
24. Correlation ID hỗ trợ điều tra lỗi mà không làm lộ stack trace cho client như thế nào?
25. Khi nào dùng `AsNoTracking`, projection và `AsSplitQuery` trong query hiện tại?
26. Bounded pagination và deterministic sorting ngăn lỗi dữ liệu nào?
27. Vì sao không thêm index nếu chưa có query pattern và số liệu chứng minh?
28. SQL Server integration test đem lại bằng chứng gì mà EF InMemory không thể cung cấp?
29. Coverage gate 80% line/60% branch có ý nghĩa gì và vì sao không đặt mục tiêu 100%?
30. Docker migration one-shot giảm rủi ro deploy nhiều instance như thế nào?
31. Những dữ liệu nào được persist bằng volume và vì sao?
32. Vì sao container API chạy bằng non-root user?
33. Vì sao hiện tại chưa thêm Redis hoặc message broker?
34. Các giới hạn nào phải xử lý trước khi scale API thành nhiều replica?
35. Ba bằng chứng nào cần trình bày để trung thực khi nói dự án sẵn sàng đưa vào CV?

## Kết Luận

Code, migration, regression, coverage, SQL Server, release, Docker Compose local và cả ba GitHub
Actions job đã đạt Definition of Done của repository. Dự án đủ bằng chứng kỹ thuật để dùng làm
portfolio backend, nhưng không được mô tả là production-ready tuyệt đối vì các giới hạn payment
provider, object storage, scale-out, managed secrets và hạ tầng cloud vẫn còn được ghi rõ ở trên.
