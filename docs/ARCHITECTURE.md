# Kiến trúc backend

## Phạm vi

Hệ thống là modular monolith: một ASP.NET Core API process và một SQL Server database. Client và
container orchestration ngoài Docker Compose local được chủ ý đặt ngoài repository này.

Solution tách các project `Domain`, `Application`, `Infrastructure`, API host, unit test và
integration test. Project reference cưỡng chế dependency direction trong khi ứng dụng vẫn được
triển khai thành một process.

## Hướng phụ thuộc

```text
HTTP request
    |
API controller / middleware
    |
Application service / validation / DTO ------> Domain entity / policy
    |                                               ^
    v                                               |
Application persistence contract <------ Infrastructure adapter
                                        (EF Core, SQL Server, file, SMTP)
```

`Program.cs` là composition root. Controller không chứa business Transaction logic. Application
service sở hữu use-case orchestration và commit boundary. Repository contract theo feature cô lập
query và persistence detail, còn SQL locking nằm tường minh tại data boundary. Dự án triển khai
thành một process; đây không phải hệ thống microservice.

Application dùng repository, `IUnitOfWork`, `IDataConsistencyService` và `IAppTransaction`. EF Core
query composition, SQL Server Transaction object, lock hint và provider exception type chỉ được
triển khai trong Infrastructure. Web request context, JWT generation, password hashing và local
file storage cũng được Application sử dụng qua port và do API hoặc Infrastructure triển khai.
Architecture regression test từ chối tham chiếu EF Core, ASP.NET Core và security library cụ thể
trong `Application`.

Compiler cưỡng chế `Application -> Domain` và `Infrastructure -> Application + Domain`; API host
tham chiếu cả hai để ghép ứng dụng. Application service và Infrastructure adapter tự đăng ký qua
`DependencyInjection` tương ứng. API host ghép các module cùng đăng ký web, security và
configuration.

`AppDbContext` tự phát hiện các `IEntityTypeConfiguration<T>` trong
`src/ECommerceBackend.Infrastructure/Data/Configurations`. Index, constraint, relationship và
authorization seed data nằm tại persistence boundary này.

## Bất biến domain

`Order`, `Payment`, `Shipment` và `ReturnRequest` chỉ cho thay đổi state qua domain method; lifecycle
và monetary setter là private. `OrderPricingPolicy` xác minh decimal scale, amount limit được hỗ
trợ và total nhất quán trước khi order bị thay đổi. `InventoryPolicy` là rule boundary duy nhất cho
reserve/release stock và trả về chính xác quantity movement cùng resulting balance để ghi immutable
inventory ledger.

Domain rule ném `DomainRuleViolationException` với code ổn định. Application guard chuyển lỗi đó
sang HTTP 400 hoặc 409 hiện có mà không làm mất domain code. Application service vẫn sở hữu
authorization, locking, Transaction orchestration và persistence; service không hiện thực lại
aggregate state rule.

Business timestamp trong checkout, order lifecycle và Payment Webhook lấy từ `TimeProvider` được
inject. Một UTC timestamp được capture và tái sử dụng cho mọi record sinh bởi cùng business event,
giúp history và ledger xác định trong test và nhất quán trong storage. Database check, unique index
và Row Version tiếp tục là lớp bảo vệ bổ sung bên dưới domain rule.

## Các module

- Auth: register, constant-work login, timed account lockout, password reset dùng một lần,
  token-family rotation, reuse detection, logout và logout-all.
- Users: profile, password change, administration có phân trang, role assignment và bảo vệ Admin
  cuối cùng.
- Catalog: category hierarchy, product, image, search, filter và paging.
- Cart: một cart mỗi user, product line duy nhất, tối đa 50 product khác nhau và kiểm tra
  availability theo giá hiện tại. Legacy cart quá lớn vẫn đọc và xóa được nhưng không thể quote
  hoặc checkout cho đến khi giảm về giới hạn. Cart read không có side effect: cart legacy chưa tồn
  tại trả empty response; registration hoặc mutation đầu tiên mới persist cart.
- Orders: idempotent checkout, order snapshot, state transition, shipment, cancellation và return
  workflow.
- Pricing: server-side quote, shipping/tax policy, promotion limit và immutable redemption record.
- Payments: state machine tập trung, immutable status history, COD adapter và signed/idempotent
  Webhook processing.
- Inventory: current balance cùng immutable stock movement ledger.
- Reports: bounded UTC order/payment cohort, cash flow, delivered-product ranking và low-stock
  snapshot.
- Notifications: Transactional Outbox, retry/dead-letter dispatch và SMTP sender cấu hình được.

Public application service interface là facade ổn định cho controller và hosted worker. Các phần
Auth, Order và Operations lớn được ghép từ use case tập trung: registration/session/password reset,
checkout/order query/lifecycle command, dead-letter/audit/retention operation. Facade không sở hữu
`DbContext` hoặc Transaction dependency. Session, shipment, order cancellation và return command
có use case riêng. Checkout vẫn là use case sở hữu Transaction và giao việc tải cart, tạo aggregate
cùng staging persistence cho collaborator tập trung.

Application source được tổ chức dưới `Features/<Capability>`. Mỗi capability đặt DTO, validator,
service contract, use case và repository contract cùng nhau; transaction, consistency và
request-context port dùng chung nằm dưới `Interfaces`. Namespace hiện có được giữ ổn định nên việc
tổ chức vật lý không đổi public contract hoặc dependency direction.

Repository được thiết kế theo feature thay vì generic repository. Contract expose business query
và persistence operation, không làm lộ `DbSet` hoặc `IQueryable`. Application service giữ
Transaction orchestration và gọi `IUnitOfWork` tại cùng commit point của use case; mọi repository
trong một request dùng chung scoped `AppDbContext`.

## Ranh giới kiểm thử

`ECommerceBackend.UnitTests` chỉ tham chiếu Application và Domain. Project này kiểm tra validator,
domain invariant, policy và state machine mà không host API hoặc persistence adapter.

`ECommerceBackend.IntegrationTests` tham chiếu API composition root và được tổ chức theo feature.
EF Core InMemory test chỉ xác minh service/repository composition; chúng không được dùng làm bằng
chứng cho relational constraint, Transaction hoặc concurrency. Test có tag
`SqlServerIntegration`, `SqlServerRecoveryIntegration` và `SqlServerPerformance` dùng database SQL
Server cô lập cho các bảo đảm đó. OpenAPI và architecture contract test nằm trong project này vì
chúng xác minh hệ thống đã ghép thay vì một class độc lập.

## Luồng checkout

```text
Tra cứu Idempotency-Key
  -> khóa cart
  -> tra cứu lại Idempotency-Key
  -> từ chối cart vượt giới hạn Transaction 50 dòng
  -> khóa product theo thứ tự ID ổn định
  -> khóa promotion và kiểm tra lại giới hạn tổng/theo Customer
  -> xác minh active product, giá, stock và server-side quote
  -> tạo Pending Order + recipient/OrderDetails snapshot + Payment + StatusHistory
  -> snapshot promotion, shipping method và mọi money component
  -> tăng promotion usage + thêm PromotionRedemption
  -> đặt inventory hold expiration có giới hạn
  -> reserve stock + thêm InventoryTransaction
  -> xóa cart
  -> một SaveChanges + commit
```

Cùng user và Idempotency key sẽ nhận lại order ban đầu. Tái sử dụng key với address, note, payment
method, shipping method hoặc promotion khác trả `409 Conflict`. `POST /api/v1/orders/quote` chỉ
mang tính thông tin và hết hạn sau khoảng cấu hình. Checkout không tin total từ client: quote được
tính lại sau khi các row liên quan đã bị khóa. Promotion usage được dùng khi order commit và không
được hoàn lại khi hủy đơn.

Client có thể gửi `ExpectedTotalAmount` từ quote gần nhất. Checkout trả
`409 checkout_price_changed` trước mutation nếu authoritative total đã thay đổi. Ngưỡng miễn phí
standard shipping và tax rate cấu hình áp dụng trên merchandise subtotal sau discount; shipping
không nằm trong taxable amount.

## Trạng thái đơn hàng và thanh toán

```text
Order: Pending -> Confirmed -> Shipping -> Delivered -> ReturnRequested
         |           |           |
         +-----------+           +-> DeliveryFailed -> Shipping
                                      |
                                      +-> Cancelled

ReturnRequested -> ReturnApproved -> Returned -> Refunded
        |
        +-> Delivered (rejected)

Payment: Pending <-> RequiresAction <-> Processing
            |              |               |
            +--------------+---------------> Paid -> PartiallyRefunded -> Refunded
            |              |
            +--------------+---------------> Failed / Cancelled

COD đạt Paid khi Order là Delivered và đạt Cancelled khi Order là Cancelled.
```

Các decision table sau là nguồn sự thật cho command và test:

| Trạng thái order hiện tại | Trạng thái tiếp theo được chấp nhận | Điều kiện bắt buộc |
| --- | --- | --- |
| `Pending` | `Confirmed`, `Cancelled` | Cancellation yêu cầu payment chưa là `Paid` hoặc `Refunded` |
| `Confirmed` | `Shipping`, `Cancelled` | Dispatch sở hữu `Shipping`; cancellation yêu cầu payment chưa là `Paid` hoặc `Refunded` |
| `Shipping` | `Delivered`, `DeliveryFailed` | Shipment workflow sở hữu delivery; failure yêu cầu operational note |
| `DeliveryFailed` | `Shipping`, `Cancelled` | Retry phải dùng carrier và tracking number hiện có; cancellation yêu cầu payment chưa là `Paid` hoặc `Refunded` |
| `Delivered` | `ReturnRequested` | Payment phải là `Paid` và request còn trong return window |
| `ReturnRequested` | `ReturnApproved`, `Delivered` | Approval tiếp tục return; rejection khôi phục `Delivered` |
| `ReturnApproved` | `Returned` | Hàng đã duyệt phải được nhận và kiểm tra |
| `Returned` | `Refunded` | Payment và return request phải đã là `Refunded` |
| `Cancelled` | Không có | Terminal state |
| `Refunded` | Không có | Terminal state |

Tại domain boundary, chuyển sang state hiện tại là idempotent no-op. API command vẫn có thể từ
chối generic transition khi một shipment, return hoặc refund command chuyên biệt sở hữu transition.

| Trạng thái payment hiện tại | Trạng thái tiếp theo được chấp nhận | Hành vi retry |
| --- | --- | --- |
| `Pending` | `RequiresAction`, `Processing`, `Paid`, `Failed`, `Cancelled` | Create/reconcile/Webhook retry là idempotent |
| `RequiresAction` | `Pending`, `Processing`, `Paid`, `Failed`, `Cancelled` | Provider observation sau có thể đẩy payment tiến lên hoặc về pending |
| `Processing` | `Pending`, `RequiresAction`, `Paid`, `Failed`, `Cancelled` | Reconciliation sửa event bị lỡ dưới lease |
| `Paid` | `PartiallyRefunded`, `Refunded` | Refund amount và currency gốc được validate trước transition |
| `PartiallyRefunded` | `Refunded` | Cumulative refund không vượt paid amount |
| `Failed` | Không có | Terminal state |
| `Cancelled` | Không có | Terminal state |
| `Refunded` | Không có | Terminal; provider event khác được audit nhưng không gửi notification mới |

| Tình huống shipment | Kết quả |
| --- | --- |
| Confirmed order chưa có shipment | Tạo shipment và chuyển sang `Shipping` |
| Shipping order có cùng carrier/tracking | Idempotent replay |
| Shipping order có carrier/tracking khác | `409 shipment_identity_mismatch` |
| Delivery failed với cùng carrier/tracking | Ghi một lần thử `Shipping` khác |
| Delivery failed với carrier/tracking khác | `409 shipment_identity_mismatch` |
| Replay shipment đã delivered | Idempotent replay; COD chỉ được thu một lần |

| Tình huống return | Kết quả |
| --- | --- |
| Order delivered, paid, còn trong thời hạn và chưa có request | Tạo một request `Pending` và chuyển order sang `ReturnRequested` |
| Request hiện có cùng normalized reason | Idempotent replay tại outcome hiện tại |
| Request hiện có với reason khác | `409 return_request_already_exists` |
| Request bị từ chối | Terminal; không tạo request thứ hai cho cùng order |
| Pending request được duyệt/từ chối | Chuyển sang `Approved`, hoặc `Rejected` và khôi phục order về `Delivered` |
| Approved request được nhận | Hoàn stock một lần và chuyển request/order sang `Received`/`Returned` |
| Received request được refund với cùng reference | Hoàn tất một lần, replay trả kết quả đã lưu |
| Refunded request replay bằng reference khác | `409 refund_reference_mismatch` |

Stock được giữ khi order là `Pending`. Shipment dispatch yêu cầu carrier và tracking number.
Delivery failure tiếp tục giữ stock; Staff có thể retry cùng shipment hoặc hủy. Customer return
request bị giới hạn bởi `Returns:ReturnWindowDays`; Staff duyệt hoặc từ chối. Stock chỉ được hoàn
khi hàng đã duyệt được nhận vật lý và kiểm tra.

`POST /api/v1/orders/{id}/refund` chọn refund path theo payment method gốc. COD ghi nhận external
refund đã hoàn thành sau khi return được nhận. Card payment reserve một `PaymentRefund`, commit,
gọi Stripe ngoài SQL Transaction, sau đó hoàn tất payment, return request, order history, audit và
Outbox trong Transaction ngắn thứ hai. Reference là Idempotency key; partial/full refund giữ
snapshot currency gốc và VND base, còn cumulative refund không vượt captured amount.

Pending COD order hết hạn sau thời gian giữ cấu hình. Expiration worker chọn batch có giới hạn theo
`(Status, ExpiresAt, Id)`, rồi khóa từng order và kiểm tra lại state trong Transaction. Expiration
được biểu diễn bằng `Cancelled` cùng `CancellationReason=SystemExpired` và `ExpiredAt`, nên API
consumer hiện tại không cần enum mới. Customer cancellation chỉ áp dụng cho owned `Pending` order.
Checkout serialize theo customer cart và từ chối tạo mới khi vượt pending-order limit cấu hình.

## Payment Webhook

`GET /api/v1/payments/methods` là public capability contract cho checkout client. Endpoint chỉ liệt
kê method có checkout provider đã đăng ký; adapter chỉ có Webhook không được công bố. Checkout vẫn
resolve method phía server và từ chối provider chưa đăng ký, vì vậy chỉ thêm enum value không thể
bật một payment path chưa hoàn chỉnh.

`POST /api/v1/payments/webhooks/{providerCode}` đọc strict UTF-8 raw body có giới hạn. Generic HMAC
adapter xác minh `HMAC_SHA256(secret, eventId + "." + rawBody)` từ `X-Payment-Signature`;
`X-Payment-Event-Id` là duy nhất theo provider. Tái sử dụng event ID với content khác trả `409`.
Replay trả kết quả đã lưu cho event ban đầu dù payment đã chuyển state sau đó.

Generic HMAC adapter chỉ là Development/Testing contract sample và bị từ chối trong Production.
Stripe có provider-specific adapter cho tạo/truy vấn PaymentIntent, refund và signed Webhook.
Stripe bị tắt nếu thiếu test credential hoặc Webhook secret; deterministic test chứng minh contract
nhưng không phải bằng chứng đã chạy external sandbox.

Mặc định Webhook processing chỉ giữ SHA-256 payload hash, không giữ raw body. Chỉ bật
`PaymentWebhooks:GenericHmac:RetainRawPayload=true` cho điều tra có thời hạn sau khi xác nhận provider
payload không chứa dữ liệu cần tối thiểu hóa.

Provider transition gồm `RequiresAction`, `Processing`, `Paid`, `Failed`, `Cancelled`,
`PartiallyRefunded` và `Refunded`. Mỗi event hợp lệ được audit; replay hoặc event giữ payment ở cùng
state không tạo status-history row hay notification khác. Remote gateway I/O luôn chạy ngoài
inventory/order SQL Transaction.

Capture event phải khớp server-side amount và currency. Refund event phải dùng payment currency
gốc và không được đẩy cumulative refund vượt captured amount. Provider occurrence timestamp không
được trước lúc tạo payment, refund không được trước payment capture, và timestamp vượt future-clock
tolerance cấu hình bị từ chối.

Order lifecycle update và Payment Webhook cùng khóa `Order -> Payment`; cancellation sau đó khóa
product theo thứ tự GUID ổn định. Quy tắc này ngăn cancellation và capture commit tổ hợp order
`Cancelled` nhưng payment `Paid` không hợp lệ.

## Transactional Outbox

Order placement, order status change và Payment Webhook thêm notification message trong cùng
database Transaction với business data. Background dispatcher claim message atomically, retry với
exponential backoff và dead-letter sau số lần thử cấu hình. Delivery là at-least-once; notification
adapter nhận Outbox ID làm Idempotency key. Enqueue, lease, completion, retry và backlog-health
timestamp dùng UTC clock được inject.

SMTP message cũng dùng RFC `Message-ID` xác định từ Outbox ID, vì vậy mọi retry của cùng message có
cùng delivery identity. Downstream mail system nhận được tín hiệu deduplication ổn định, nhưng dự án
không tuyên bố exactly-once: process có thể dừng sau khi SMTP nhận message và trước khi database ghi
completion. Khi lease hết hạn, dispatcher chủ ý gửi lại message với cùng `Message-ID`.

Dự án không lưu provider-delivery receipt riêng vì SMTP không cung cấp portable idempotent
acknowledgement contract. Provider-specific delivery tracking chỉ nên thêm khi dùng email API có
contract và operational requirement đủ để cưỡng chế.

Khi `Outbox:RequireProcessing=true`, readiness còn yêu cầu dispatcher heartbeat gần đây. Điều này
phát hiện dispatcher dừng trước khi backlog vượt age threshold.

Admin có thể xem dead letter mà không nhận payload và redrive từng message. Redrive khóa message,
kiểm tra lại terminal state, reset retry state và thêm audit event trong một Transaction. Request
đồng thời hoặc lặp là idempotent.

## Vận hành và audit

Mutation đặc quyền cho role, catalog, product image và order status thêm `AuditEvent` trước khi
Transaction commit. Event chứa metadata có giới hạn, actor, forwarded client IP và request
correlation ID; secret và request payload bị loại bỏ. Endpoint Operations chỉ dành cho Admin cung
cấp audit/dead-letter read có phân trang và upload reconciliation có giới hạn.

Upload reconciliation so sánh `/uploads/products` với `ProductImages`. Dry-run là mặc định. Cleanup
chỉ chạm tới application-generated orphan name cũ hơn grace period cấu hình; file được tham chiếu
nhưng bị thiếu chỉ được báo cáo và không bị tự động xóa khỏi database.

## Quy tắc nhất quán

- Cart mutation được serialize theo cart.
- Order lifecycle, shipment, return và Webhook mutation khóa order trước dependent row.
- Checkout và cancellation khóa product row theo thứ tự GUID ổn định.
- Checkout preflight mọi cart line sau khi lấy product lock và trước khi thay đổi stock, order,
  payment hoặc cart; một line không khả dụng vì vậy để toàn bộ cart không đổi.
- Catalog write khóa category trước product; update nhiều category khóa category GUID tăng dần.
- Product-image mutation dùng product row làm serialization boundary và tải image state sau lock.
- Product administration ghi stock qua `InventoryPolicy`; mutation trả về được persist nguyên vẹn
  trong inventory ledger.
- Product, category, order, payment, user và Refresh Token row dùng Row Version concurrency.
- Category uniqueness được cưỡng chế cả trong code và filtered SQL unique index.
- Historical order name và price lấy từ `OrderDetails`, không lấy product row hiện tại.
- Mọi stock change do product administration hoặc order đều thêm inventory entry.
- Database constraint ngăn order line, payment outcome và order inventory movement trùng. Order
  lifecycle write serialize theo locked order row nên delivery attempt lặp vẫn có thể giữ nhiều
  history entry `Shipping` và `DeliveryFailed`.
- Payment và Webhook status outcome được lưu thành immutable audit data với constraint state/value
  hợp lệ.

## Ngữ nghĩa báo cáo

`GET /api/v1/reports/sales-summary` dùng half-open UTC range `[From, To)` và giới hạn request ở 366
ngày. `TotalOrders` và `OrdersByStatus` là cohort order được tạo trong khoảng. `DeliveredOrders`,
`CancelledOrders` và top product dùng transition time tương ứng trong `OrderStatusHistory`. Gross
cash collected dùng `PaidAt`; refund dùng occurrence time của payment history `Refunded`; net
revenue bằng gross collected trừ refund trong khoảng.

Khi caller bỏ report date, service capture UTC clock một lần và dùng window xác định
`[now - 30 days, now)`. Range không hợp lệ, range quá lớn, low-stock threshold và top-product limit
trả stable business error code. SQL Server integration test cố định quy tắc bao gồm `From`, loại
`To`, ngữ nghĩa refund occurrence và historical product-name snapshot.

Top product chỉ gồm order delivered trong khoảng, aggregate một lần theo `ProductId` và dùng latest
historical name snapshot trong cohort. Low-stock count là current inventory snapshot theo threshold
yêu cầu, không phải historical value. Top-product revenue là gross merchandise value từ
`OrderDetails` snapshot; order-level discount, shipping fee và tax chủ ý không được phân bổ xuống
từng line.

## Phân quyền

JWT role chỉ mang tính thông tin; protected administration endpoint yêu cầu permission claim.
Access-token validation còn xác minh user token version và active Refresh Token family trong SQL
Server. Password hoặc role change thu hồi mọi session hiện có ngay lập tức. Customer cart, order,
cancellation và return command luôn scope lookup theo user đã xác thực; cross-owner identifier trả
`404` thay vì tiết lộ resource tồn tại.

Role assignment cấm self-change và chạy dưới isolation `Serializable` với Transaction-scoped
application lock `ECommerceBackend.RoleAssignment`. Hai demotion đồng thời vì vậy không thể xóa
active `Admin` cuối cùng; command thứ hai thấy commit của command đầu và từ chối xóa Admin còn lại.

User account là serialization boundary cho session mutation. Login, refresh, logout, logout-all,
password change và role change khóa user trước Refresh Token; lock order `User -> RefreshToken` cố
định ngăn concurrent refresh sống sót sau session revocation. Refresh Token rotation và revocation
là domain method với private mutation setter; token activity được đánh giá tại UTC timestamp tường
minh.

Identity service và Admin bootstrapper dùng `TimeProvider` được inject. Token creation, rotation,
family revocation, password change và JWT expiry vì vậy dùng security timestamp xác định. Identity
conflict trả stable error code trong khi giữ HTTP 400, 401 và 409 contract hiện có.

Login thực hiện BCrypt verification cho cả username tồn tại và không tồn tại, trả cùng unauthorized
contract và áp dụng lockout tự hết hạn sau nhiều lần thất bại. Password-reset request cố ý trả cùng
success response cho email đã đăng ký và không tồn tại. Reset token là random, chỉ lưu SHA-256 hash,
có thời hạn, dùng một lần và được serialize theo user. Hoàn tất reset tăng user token version và
thu hồi mọi Refresh Token family.

Password-reset notification dùng Data Protection protected Outbox payload nên raw reset token
không được lưu dưới dạng JSON đọc được. Email verification có token hash, thời hạn và single-use
riêng, đồng thời ghi `User.EmailVerifiedAt`. Trạng thái này chủ ý chưa phải điều kiện sign-in; bật
bắt buộc cần enrollment và compatibility policy rõ cho user hiện có.

## Vận hành

Mỗi request nhận response header `X-Correlation-ID`. Caller-provided ID chỉ được chấp nhận khi dài
1-128 ký tự ASCII gồm chữ, số, dấu chấm, underscore hoặc hyphen; nếu không, server dùng current
activity trace ID hoặc tạo mới. Cùng giá trị trở thành `traceId` trong error response và Serilog
property trong request, console và rolling-file log. Activity ID dùng W3C format; `TraceId` và
`SpanId` là structured log property riêng cho cross-service tracing.

Mọi response còn có `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
`Referrer-Policy: no-referrer` và restrictive permissions policy. Các header được thêm trước khi
static product image được phục vụ và trước API middleware.

Catalog read truyền request cancellation vào EF Core và split collection include để tránh
cartesian product khi product có nhiều image. Query count, duration và returned-item count được phát
mà không ghi search text. Tag `catalog.outcome` có giới hạn phân biệt query thành công, bị cancel và
thất bại, để quyết định tối ưu không chỉ đo happy path. Full-text search và response cache tiếp tục
phụ thuộc bằng chứng đo vì operational cost và invalidation rule chưa được chứng minh cần thiết.

Product và order list endpoint hiện có giữ response contract v1. Client chỉ render list row có thể
dùng `/api/v1/products/summaries`, `/api/v1/orders/my/summaries` và
`/api/v1/orders/summaries`. Các endpoint này project list field ngay trong SQL và không materialize
product image collection, order detail, payment history hoặc status history. Detail endpoint tiếp
tục là nguồn complete aggregate view.

Exception, MVC validation, authentication, authorization và rate-limit failure dùng chung contract
`application/problem+json`. Standard `ProblemDetails` field tồn tại cùng compatibility field ổn định
`message`, `code`, `traceId`, `details` và `errors`.

Fixed-window limit được bind từ section `RateLimiting` đã validate khi startup; thay limit cần
restart ứng dụng và không đổi in-process deployment boundary. Rejected fixed-window lease trả phần
thời gian còn lại trong header `Retry-After` dạng delta-seconds cùng ProblemDetails
`rate_limit_exceeded`. Title và message cho client dùng tiếng Việt; `code` giữ English identifier
ổn định. Version 1 có tại `/api/v1`; route `/api` cũ mặc định version 1 và được contract test bảo vệ
tương thích ngược.

`/health/live` chỉ chứa in-process self check. `/health/ready` kiểm tra SQL Server, quyền ghi
product-image storage, Outbox processing, pending-order expiration và data-retention worker state.
Local-storage probe tạo, flush rồi xóa unique temporary file; không để lại upload record hoặc
database row. Dependency check dùng chung timeout
`HealthChecks:DependencyTimeoutSeconds` đã validate khi startup và truyền timeout/request
cancellation qua I/O. Public health endpoint chỉ trả aggregate status; dữ liệu từng check chỉ dành
cho Admin tại `/health/details`.

Client-aborted request được ghi là cancellation thay vì internal server error và không cố ghi JSON
vào connection đã đóng. Exception sau khi response header đã bắt đầu được rethrow vì thay response
đang ghi dở sẽ phá HTTP contract.

## Ranh giới chủ ý

- Local image storage được giữ sau asynchronous `IProductImageStorage` port cho phạm vi triển khai
  hiện tại; `IUploadService` tiếp tục sở hữu image validation và product-image rule.
- Static serving chỉ dành cho generated product image dưới `/uploads/products`; chỉ JPG, PNG và WEBP
  được phục vụ, response tắt MIME sniffing.
- SQL command timeout cấu hình qua `Database:CommandTimeoutSeconds`. EF Core retry không bật toàn cục
  vì checkout, order lifecycle và Webhook sở hữu explicit Transaction và lock; retry phải bọc toàn
  business operation nếu được thêm sau này.
- COD luôn khả dụng. Stripe card checkout chỉ khả dụng khi provider được bật và cấu hình từ bên
  ngoài; generic HMAC provider chỉ dùng Development/Testing.
- Currency snapshot hỗ trợ VND, USD và EUR với VND là reporting base. CurrencyAPI adapter dùng
  timeout, process-local cache, single-flight và stale fallback có giới hạn.
- Shipping fee, discount và tax được tính từ server-side rule cấu hình; live carrier pricing và
  jurisdiction-specific tax ngoài phạm vi hiện tại.
- Payment adapter được validate trước persistence: provider code phải route-safe, checkout method
  phải được định nghĩa, initial state phải theo payment state machine và Webhook-capable checkout
  provider phải trả transaction ID có giới hạn.
- Product variant cùng live carrier/tax integration là future domain module.
