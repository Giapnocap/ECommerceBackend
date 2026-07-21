# Implementation Roadmap

Roadmap nÃ y Ä‘i theo 8 phase Ä‘Ã£ thá»‘ng nháº¥t.

## Phase 1 - Chuáº©n HÃ³a Ná»n Táº£ng

Tráº¡ng thÃ¡i: hoÃ n táº¥t.

- RÃ  soÃ¡t cáº¥u trÃºc project.
- TÃ¡ch dependency registration sang `API/Extensions`.
- ThÃªm README hÆ°á»›ng dáº«n cháº¡y dá»± Ã¡n.
- Ghi láº¡i quyáº¿t Ä‘á»‹nh ká»¹ thuáº­t ná»n.
- Build vÃ  format verification.

## Phase 2 - Auth & Authorization

Tráº¡ng thÃ¡i: hoÃ n táº¥t.

Má»¥c tiÃªu:

- ThÃªm refresh token.
- ThÃªm endpoint refresh/logout.
- Chuáº©n hÃ³a token payload.
- Chuáº©n bá»‹ ná»n cho permission claims.

## Phase 3 - Mapping & Response Format

Tráº¡ng thÃ¡i: hoÃ n táº¥t.

Má»¥c tiÃªu:

- ThÃªm `Application/Mappings/MappingProfile.cs`.
- ÄÄƒng kÃ½ AutoMapper.
- Chuyá»ƒn mapping thá»§ cÃ´ng sang AutoMapper á»Ÿ cÃ¡c service phÃ¹ há»£p.
- Chuáº©n hÃ³a success/error response náº¿u cáº§n.

Káº¿t quáº£:

- Mapping entity sang response DTO Ä‘Ã£ Ä‘Æ°á»£c táº­p trung trong AutoMapper profile.
- CÃ¡c service khÃ´ng cÃ²n chá»©a hÃ m `MapToResponse` trÃ¹ng láº·p.
- Giá»¯ nguyÃªn success contract hiá»‡n cÃ³ vÃ  error contract thá»‘ng nháº¥t Ä‘á»ƒ trÃ¡nh breaking change.

## Phase 4 - Product, Upload, Category

Tráº¡ng thÃ¡i: hoÃ n táº¥t.

Má»¥c tiÃªu:

- TÃ¡ch `IUploadService`/`UploadService`.
- Chuáº©n hÃ³a validation áº£nh.
- Cá»§ng cá»‘ rule áº£nh chÃ­nh.
- Cá»§ng cá»‘ rule category parent-child.

Káº¿t quáº£:

- TÃ¡ch upload áº£nh sang `IUploadService`/`UploadService` mÃ  khÃ´ng Ä‘á»•i API route.
- Kiá»ƒm tra extension, MIME type, chá»¯ kÃ½ file vÃ  giá»›i háº¡n dung lÆ°á»£ng cáº¥u hÃ¬nh Ä‘Æ°á»£c.
- Äá»“ng bá»™ file system/database báº±ng transaction vÃ  unique filtered index cho áº£nh chÃ­nh.
- Chuáº©n hÃ³a rule giÃ¡ sáº£n pháº©m, query params vÃ  category tá»‘i Ä‘a 2 cáº¥p.
- ThÃªm migration `HardenProductImagesAndCategories`.

## Phase 5 - Cart & Order

Tráº¡ng thÃ¡i: hoÃ n táº¥t.

Má»¥c tiÃªu:

- Chuáº©n hÃ³a order status state machine.
- Kiá»ƒm tra ká»¹ rollback/stock/cancel.
- HoÃ n thiá»‡n flow mua hÃ ng end-to-end.

Káº¿t quáº£:

- Chuáº©n hÃ³a state machine `Pending -> Confirmed -> Shipping -> Delivered`, cho phÃ©p há»§y tá»« `Pending` hoáº·c `Confirmed`; thao tÃ¡c láº·p cÃ¹ng tráº¡ng thÃ¡i lÃ  idempotent.
- ÄÆ¡n má»›i Ä‘Æ°á»£c xÃ¡c nháº­n ngay vÃ¬ pháº¡m vi hiá»‡n táº¡i chÆ°a tÃ­ch há»£p thanh toÃ¡n; `Pending` Ä‘Æ°á»£c giá»¯ cho dá»¯ liá»‡u cÅ© hoáº·c cá»•ng thanh toÃ¡n trong tÆ°Æ¡ng lai.
- Checkout khÃ³a cart vÃ  product theo thá»© tá»± á»•n Ä‘á»‹nh, kiá»ƒm tra láº¡i giÃ¡/tá»“n kho trong transaction, trá»« kho vÃ  xÃ³a cart item trong cÃ¹ng má»™t láº§n lÆ°u.
- Há»§y Ä‘Æ¡n hoÃ n kho Ä‘Ãºng má»™t láº§n; rollback giá»¯ nguyÃªn cart, order vÃ  tá»“n kho khi checkout tháº¥t báº¡i.
- ThÃªm row version, unique cart-item index, check constraints vÃ  migration `HardenCartAndOrderFlow`.
- Smoke test toÃ n luá»“ng vÃ  kiá»ƒm thá»­ hai checkout Ä‘á»“ng thá»i Ä‘á»u Ä‘áº¡t; khÃ´ng oversell, deadlock hay lá»—i `500`.

- Bo sung hardening cho money precision `decimal(18,2)`, tinh tong don hang co guard overflow, fallback cart creation race va paging on dinh.

## Phase 6 - Swagger & API Documentation

Trang thai: hoan tat.

Má»¥c tiÃªu:

- ThÃªm default response operation filter.
- Bá»• sung XML comments quan trá»ng.
- Cáº­p nháº­t `ECommerceBackend.http` thÃ nh bá»™ request demo Ä‘áº§y Ä‘á»§.

Ket qua:

- Them `DefaultResponseOperationFilter` de Swagger tu hien thi schema loi chuan cho validation/auth/not found/conflict/server error.
- Gan success response schema cho cac endpoint chinh bang `ProducesResponseType`.
- Giu XML comments tu csproj va controller summaries de Swagger doc doc duoc.
- Cap nhat `ECommerceBackend.http` thanh request collection theo luong admin, product/category, customer, cart va order.
- Dong bo Bearer security va 401/403 trong OpenAPI voi authenticated fallback policy.
- Bo sung response 413/429 cho payment webhook va regression test cho danh sach endpoint public.
- Publish artifact khong con chua appsettings local hoac configuration template.

## Phase 7 - Tests

Trang thai: hoan tat.

Má»¥c tiÃªu:

- Táº¡o `ECommerceBackend.Tests`.
- ThÃªm test cho Auth, Product, Cart, Order.
- Cháº¡y Ä‘Æ°á»£c `dotnet test`.

Ket qua:

- Tao `ECommerceBackend.Tests` va them vao solution.
- Bo test hien tai co 133 unit test cho auth/session, user/category/product/cart/order, payment webhook,
  reporting, middleware va outbox; 10 SQL Server integration flows duoc chay theo environment flag.
- Dung EF Core InMemory cho service tests khong phu thuoc SQL Server.
- Dung SQL Server database tam cho lock, transaction, migration, outbox va race-condition tests.
- Tao XPlat code coverage report trong final quality gate.
- Coverage baseline cho code viet tay la 54.79% line va 37.10% branch; generated migrations va top-level startup duoc loai bang `coverage.runsettings`.
- Main project exclude thu muc test de tranh SDK glob compile test files vao app.
- `dotnet test --no-restore` pass.

## Phase 8 - Production Readiness

Trang thai: hoan tat.

Ket qua:

- Them startup validation cho JWT/CORS va fail-fast khi Production con dung development JWT key.
- Them health endpoints `/health/live`, `/health/ready`, `/health`.
- CORS duoc resolve theo moi truong, Development co localhost fallback, Production yeu cau origin cu the.
- Swagger chi bat mac dinh o Development; Production can `Swagger:Enabled=true` neu muon mo.
- AutoMapper co the nhan `AutoMapper:LicenseKey` tu config/secret store.
- Them `appsettings.Production.example.json` va `docs/DEPLOYMENT.md`.
- Chuan hoa exception, validation, authentication va rate-limit errors bang `ProblemDetails`.
- Giu cac compatibility fields `message`, `code`, `traceId`, `details`, `errors` de khong gay frontend regression.
- Them correlation ID, Serilog request logging, deterministic outbox clock va publish smoke tests.

Má»¥c tiÃªu:

- Secret/config production.
- Health check.
- CORS theo mÃ´i trÆ°á»ng.
- Deploy/migration docs.

## Phase 9 - Backend And Database Completion

Trang thai: hoan tat.

Ket qua:

- Refresh-token family, atomic rotation, reuse detection, logout-all va immediate session invalidation.
- Bootstrap admin theo runtime secret; migration khong con seed mat khau production co dinh.
- Permission policies va rate limiting cho auth/refresh/upload.
- Category normalized uniqueness, row version va transaction lock voi product.
- Checkout idempotency key, order number, product-name snapshot va money breakdown.
- COD payment, order status history va inventory transaction ledger.
- Inventory/low-stock API va sales summary report API.
- Migration backfill bao toan user/order cu va SQL Server integration test co database tam.
- Tai lieu `ARCHITECTURE.md` va `DATABASE.md` mo ta kien truc/constraint/flow hien tai.
- Transaction/row-lock abstraction giu SQL Server details trong Infrastructure.
- COD payment adapter, generic signed webhook, webhook replay audit va provider resolver.
- Transactional outbox voi atomic claim, retry/dead-letter va SMTP/log-only notification sender.
- NuGet vulnerability audit sach cho ca API va test project sau khi nang xUnit/runner.
