# ECommerceBackend

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-Web_API-512BD4)
![Entity Framework Core](https://img.shields.io/badge/EF_Core-8.0-512BD4)
![SQL Server](https://img.shields.io/badge/SQL_Server-2022-CC2927?logo=microsoftsqlserver&logoColor=white)
![Tests](https://img.shields.io/badge/tests-xUnit-5E2B97)
[![Backend CI](https://github.com/Giapnocap/ECommerceBackend/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Giapnocap/ECommerceBackend/actions/workflows/ci.yml)

REST API cho hệ thống thương mại điện tử, xây dựng bằng ASP.NET Core 8, Entity Framework Core và
SQL Server. Dự án tập trung vào tính nhất quán dữ liệu, phân quyền, xử lý đồng thời và khả năng
quan sát của các luồng backend thực tế.

Đây là dự án portfolio theo hướng `production-oriented`, không phải tuyên bố hệ thống đã được
kiểm chứng ở quy mô production. Repository chỉ chứa backend, database, test và công cụ chạy local;
frontend và hạ tầng cloud không thuộc phạm vi dự án.

## Mục lục

- [Công nghệ](#công-nghệ)
- [Chức năng chính](#chức-năng-chính)
- [Điểm kỹ thuật backend](#điểm-kỹ-thuật-backend)
- [Kiến trúc](#kiến-trúc)
- [Luồng checkout](#luồng-checkout)
- [Xác thực và phân quyền](#xác-thực-và-phân-quyền)
- [Thanh toán](#thanh-toán)
- [Kiểm thử và chất lượng](#kiểm-thử-và-chất-lượng)
- [Khởi chạy nhanh](#khởi-chạy-nhanh)
- [Tài liệu API](#tài-liệu-api)
- [Tài liệu chi tiết](#tài-liệu-chi-tiết)
- [Giới hạn hiện tại](#giới-hạn-hiện-tại)

## Công nghệ

| Nhóm | Công nghệ |
| --- | --- |
| Nền tảng | .NET 8, ASP.NET Core Web API |
| Dữ liệu | Entity Framework Core 8, SQL Server 2022 |
| Xác thực | JWT Bearer, BCrypt, Refresh Token |
| Validation | FluentValidation |
| Quan sát | Serilog, OpenTelemetry, correlation ID, health checks |
| API | REST, Swagger/OpenAPI, API versioning, ProblemDetails |
| Kiểm thử | xUnit, ASP.NET Core integration tests, SQL Server integration tests |
| Công cụ | Docker Compose, GitHub Actions |

## Chức năng chính

- Đăng ký, đăng nhập, xác minh email, đặt lại mật khẩu và quản lý phiên đăng nhập.
- Phân vai trò `Admin`, `Staff`, `Customer` kết hợp permission policy và kiểm tra quyền sở hữu dữ liệu.
- Quản lý danh mục, sản phẩm, ảnh sản phẩm, tồn kho và inventory ledger.
- Tìm kiếm, lọc, sắp xếp, phân trang sản phẩm; quản lý giỏ hàng.
- Báo giá phía server, promotion, phí giao hàng, thuế và checkout có Idempotency.
- Quản lý vòng đời đơn hàng, giao hàng, hủy đơn, trả hàng và hoàn tiền.
- COD, Stripe PaymentIntent, Webhook có xác thực và payment reconciliation.
- Transactional Outbox cho thông báo, retry, dead-letter và redrive.
- Dashboard, báo cáo doanh thu, trạng thái đơn, sản phẩm bán chạy và tồn kho thấp.
- Audit log, đối soát file upload và data retention có kiểm soát.

## Điểm kỹ thuật backend

- Checkout chạy trong một Transaction, khóa cart và product theo thứ tự ổn định, sau đó ghi order,
  payment, inventory history và Outbox trước khi commit.
- `Idempotency-Key` ngăn tạo trùng đơn khi client retry; tái sử dụng key với nội dung khác trả
  `409 Conflict`.
- Row Version, unique constraint, SQL lock và state machine bảo vệ các luồng có race condition.
- Order lưu snapshot người nhận, sản phẩm, giá và tiền tệ để dữ liệu lịch sử không phụ thuộc catalog
  hiện tại.
- Mọi thay đổi tồn kho đều tạo inventory ledger entry với số dư sau giao dịch.
- Stripe I/O chạy ngoài SQL Transaction dài; Webhook kiểm tra chữ ký, event identity, amount và
  currency trước khi cập nhật trạng thái.
- Transactional Outbox commit cùng dữ liệu nghiệp vụ và cung cấp cơ chế gửi at-least-once.
- `ProblemDetails`, correlation ID, Serilog, OpenTelemetry và health checks hỗ trợ chẩn đoán lỗi.

## Kiến trúc

Hệ thống là modular monolith: một ASP.NET Core API process và một SQL Server database. Các project
tách trách nhiệm theo dependency direction, nhưng vẫn được triển khai thành một ứng dụng duy nhất.

```mermaid
flowchart LR
    Client[HTTP Client] --> API[API và middleware]
    API --> Application[Application use cases]
    Application --> Domain[Domain entities và policies]
    Application --> Contracts[Repository và transaction contracts]
    Infrastructure[Infrastructure adapters] -. triển khai .-> Contracts
    Infrastructure --> Database[(SQL Server)]
    Infrastructure --> External[Storage, SMTP, Stripe, CurrencyAPI]
    API -. composition root .-> Infrastructure
```

| Tầng | Trách nhiệm | Vị trí |
| --- | --- | --- |
| API | HTTP contract, middleware, authentication, authorization, Swagger | `src/ECommerceBackend` |
| Application | DTO, validation, use case, repository contract, transaction orchestration | `src/ECommerceBackend.Application` |
| Domain | Entity, state transition, business policy và invariant | `src/ECommerceBackend.Domain` |
| Infrastructure | EF Core, repository, SQL locking, external adapter và hosted worker | `src/ECommerceBackend.Infrastructure` |
| Tests | Unit test và integration test | `tests` |

`Program.cs` là composition root. Controller không sở hữu business Transaction; Application điều
phối use case và commit boundary; Domain bảo vệ invariant; Infrastructure triển khai persistence
và tích hợp bên ngoài. Repository được thiết kế theo feature, không expose `DbSet` hoặc
`IQueryable` qua Application.

Chi tiết dependency, module và consistency rule nằm trong
[tài liệu kiến trúc](docs/ARCHITECTURE.md).

## Luồng checkout

```text
HTTP request
  -> validation và authorization
  -> kiểm tra Idempotency-Key
  -> bắt đầu Transaction, khóa cart và product
  -> tính lại giá, promotion, phí giao hàng và thuế phía server
  -> tạo order/payment snapshot, giữ tồn kho, ghi history và Outbox
  -> xóa cart, SaveChanges và commit
  -> trả order đã tạo hoặc order cũ khi request được retry hợp lệ
```

Checkout không tin tổng tiền từ client. Khi stock, giá hoặc promotion không còn hợp lệ, toàn bộ
Transaction rollback; không để lại order, ledger hoặc cart ở trạng thái ghi dở.

## Xác thực và phân quyền

- Access token dùng JWT; Refresh Token chỉ được lưu dưới dạng hash, xoay vòng theo token family và
  phát hiện reuse.
- Protected request kiểm tra chữ ký JWT, token version và phiên còn hoạt động trong SQL Server.
- Đổi mật khẩu, đổi vai trò, logout-all hoặc phát hiện token reuse sẽ thu hồi các phiên liên quan.
- `Admin`, `Staff`, `Customer` cung cấp vai trò nghiệp vụ; endpoint quản trị dùng permission policy
  như `manage_users`, `manage_products`, `process_orders` và `view_reports`.
- Dữ liệu giỏ hàng, đơn hàng, hủy đơn và trả hàng được giới hạn theo chủ sở hữu; identifier của
  người dùng khác không làm lộ tài nguyên.
- Login lockout, password reset token và email verification token có thời hạn và dùng một lần.

Email verification hiện được ghi nhận nhưng chưa bắt buộc để đăng nhập. Chi tiết về session và
các sequence xác thực nằm trong [tài liệu sequence](docs/SEQUENCES.md).

## Thanh toán

COD luôn khả dụng. Thanh toán thẻ dùng Stripe PaymentIntent khi cấu hình Stripe được bật và cung
cấp credential từ môi trường bên ngoài. Payment provider được đặt sau abstraction để Application
không phụ thuộc trực tiếp vào HTTP contract của Stripe.

Payment initialization, Webhook, reconciliation và refund đều bảo vệ Idempotency và state
transition. Provider I/O không chạy trong Transaction đang giữ lock order hoặc inventory. Chi tiết
về Webhook, refund, multi-currency và failure recovery được tách sang
[tài liệu kiến trúc](docs/ARCHITECTURE.md), [sequence nghiệp vụ](docs/SEQUENCES.md) và
[báo cáo capability](FULL_UPGRADE_REPORT.md).

Stripe Test Mode, CurrencyAPI và SMTP chưa được xác minh bằng credential thật trong repository;
trạng thái kiểm chứng được ghi rõ trong
[báo cáo mức độ sẵn sàng](PRODUCTION_READINESS_REPORT.md).

## Kiểm thử và chất lượng

README chỉ công bố các gate ổn định. Số test và coverage đo được của một baseline cụ thể được lưu
tập trung trong [báo cáo mức độ sẵn sàng](PRODUCTION_READINESS_REPORT.md), tránh để số liệu giữa
README và CI bị lệch sau mỗi commit.

| Gate | Phạm vi |
| --- | --- |
| Repository security | Secret scan và NuGet advisory audit |
| Chất lượng code | `dotnet format --verify-no-changes` và Release build |
| Unit test | Application và Domain |
| Integration test | API contract, use case và adapter deterministic |
| SQL Server test | Migration, constraint, Transaction, concurrency và recovery |
| Coverage | Line coverage tối thiểu 80%, branch coverage tối thiểu 60% |
| Database | Kiểm tra model drift và tạo migration artifact |
| Đóng gói | Tạo, xác minh checksum và smoke test release package |
| Docker | Build Compose, chạy migration và kiểm tra `/health/ready` |

Workflow chính nằm tại [`.github/workflows/ci.yml`](.github/workflows/ci.yml). Performance test là
regression baseline chạy riêng theo lịch hoặc thủ công, không phải phép đo năng lực production.

## Khởi chạy nhanh

Yêu cầu: Git và Docker Desktop có Docker Compose.

```powershell
git clone https://github.com/Giapnocap/ECommerceBackend.git
Set-Location ECommerceBackend
Copy-Item .env.example .env
```

Mở `.env`, thay `MSSQL_SA_PASSWORD` và `JWT_KEY` bằng giá trị local của bạn. Không commit file
`.env`. Nếu cần tạo Admin đầu tiên, điền các biến `ADMIN_BOOTSTRAP_*`, đặt
`ADMIN_BOOTSTRAP_ENABLED=true` và dùng password dài từ 12 đến 128 ký tự, không phải placeholder.

```powershell
docker compose up --build --detach
docker compose ps
```

Compose sẽ khởi động SQL Server, chạy EF Core migration bằng service `migrate`, sau đó mới khởi
động API. Các địa chỉ mặc định:

- Swagger UI: <http://localhost:5171/swagger>
- Liveness: <http://localhost:5171/health/live>
- Readiness: <http://localhost:5171/health/ready>

Sau khi Admin được tạo thành công, đặt `ADMIN_BOOTSTRAP_ENABLED=false` và khởi động lại API. Dừng
ứng dụng nhưng giữ dữ liệu local bằng:

```powershell
docker compose down
```

Không thêm `--volumes` nếu muốn giữ database, ảnh, Data Protection keys và log local.

## Tài liệu API

- Route chuẩn là `/api/v1`; route `/api` được giữ làm alias tương thích và mặc định dùng v1.
- Swagger chỉ được bật theo cấu hình môi trường; Quick Start ở trên chạy bằng `Development` nên có
  Swagger UI.
- [`ECommerceBackend.http`](src/ECommerceBackend/ECommerceBackend.http) chứa request mẫu để chạy
  bằng Visual Studio hoặc VS Code REST Client.
- Lỗi API dùng `application/problem+json` với `code`, `message`, `traceId`, `details` và `errors`
  khi phù hợp.

## Tài liệu chi tiết

| Tài liệu | Nội dung |
| --- | --- |
| [Kiến trúc](docs/ARCHITECTURE.md) | Dependency, module, invariant, checkout, payment, Outbox và consistency |
| [ERD](docs/ERD.md) | Entity, quan hệ và constraint dữ liệu chính |
| [Sequence nghiệp vụ](docs/SEQUENCES.md) | Login, checkout, payment, giao hàng, trả hàng và reset password |
| [Kịch bản demo](docs/DEMO.md) | Luồng COD local và các nhánh cần external provider |
| [Hiệu năng](docs/PERFORMANCE.md) | Budget và regression baseline local |
| [Giám sát](docs/MONITORING.md) | Telemetry, health checks, metric và cảnh báo |
| [Runbook](docs/RUNBOOK.md) | Deploy, migration, rollback, backup, recovery và xử lý sự cố |
| [Giới hạn](docs/LIMITATIONS.md) | Phạm vi triển khai và trigger nâng cấp |
| [Báo cáo capability](FULL_UPGRADE_REPORT.md) | Những capability đã hiện thực trong source |
| [Mức độ sẵn sàng](PRODUCTION_READINESS_REPORT.md) | Baseline test/coverage và external blockers |

## Giới hạn hiện tại

- Topology được hỗ trợ là một API instance và một SQL Server database.
- Ảnh sản phẩm dùng local durable volume; rate limiter và FX cache chạy trong process.
- Outbox/SMTP bảo đảm at-least-once, không tuyên bố exactly-once delivery.
- Email verification chưa phải điều kiện đăng nhập.
- Stripe, CurrencyAPI, SMTP, OTLP collector, staging TLS và backup operation thật cần môi trường bên
  ngoài để xác minh.
- Số liệu performance là regression baseline local/CI, không phải SLA hoặc capacity forecast.

Danh sách đầy đủ nằm trong [giới hạn hệ thống](docs/LIMITATIONS.md). Trạng thái hiện tại là Release
Candidate còn external blockers, chưa phải production-verified và chưa tạo tag `v1.0.0`.
