# Backend Architecture

## Scope

The system is a modular monolith: one ASP.NET Core API process and one SQL Server database.
Client applications and container orchestration are intentionally outside this repository.

The solution uses separate `Domain`, `Application`, `Infrastructure`, API host and test projects.
Project references enforce the dependency direction while deployment remains a single process.

## Dependency Direction

```text
HTTP request
    |
API controllers / middleware
    |
Application services / validation / DTOs ------> Domain entities / policies
    |                                                  ^
    v                                                  |
Application persistence contracts <------ Infrastructure adapters
                                           (EF Core, SQL Server, files, SMTP)
```

`Program.cs` is the composition root. Controllers contain no business transaction logic.
Application services own use-case orchestration and commit boundaries. Feature-specific
repository contracts isolate query and persistence details, while SQL-specific locking remains
explicit at the data boundary. The project is deployed as one process; it is not a microservice
system.

Application code uses repositories, `IUnitOfWork`, `IDataConsistencyService` and
`IAppTransaction`. EF Core query composition, SQL Server transaction objects, lock hints and
provider exception types are implemented only in Infrastructure. An architecture regression test
rejects EF Core references under `Application`.

The compiler enforces `Application -> Domain` and
`Infrastructure -> Application + Domain`; the API host references both to compose the process.
Composition registration is split by responsibility across
`ServiceCollectionExtensions.Configuration`, `.Infrastructure`, `.Security` and `.Web`.
`AppDbContext` discovers per-entity `IEntityTypeConfiguration<T>` implementations from
`Infrastructure/Data/Configurations`; indexes, constraints, relationships and authorization seed
data are kept at that persistence boundary.

## Domain Invariants

`Order` and `Payment` expose state changes through domain methods; their lifecycle and monetary
setters are private. `OrderPricingPolicy` validates decimal scale, supported amount limits and
consistent totals before an order is mutated. `InventoryPolicy` is the single rule boundary for
order reservation and release, and returns the exact quantity movement and resulting balance for
the immutable inventory ledger.

Domain rules throw `DomainRuleViolationException` with stable codes. Application guards translate
those failures to the existing HTTP 400 or 409 contracts without losing the domain code. Application
services still own authorization, locking, transaction orchestration and persistence; they do not
reimplement aggregate state rules.

Business timestamps in checkout, order lifecycle and payment webhook flows come from the injected
`TimeProvider`. One captured UTC timestamp is reused for all records produced by the same business
event, which keeps histories and ledger entries deterministic in tests and consistent in storage.
Database checks, unique indexes and row versions remain defense-in-depth beneath these domain rules.

## Modules

- Auth: register, constant-work login, timed account lockout, single-use password reset,
  token-family rotation, reuse detection, logout and logout-all.
- Users: profile, password changes, paged administration, role assignment and last-admin protection.
- Catalog: category hierarchy, products, images, search, filtering and paging.
- Cart: one cart per user, unique product lines and current-price availability checks.
- Orders: idempotent checkout, order snapshots, state transitions and cancellation.
- Pricing: server-side quote, shipping/tax policy, promotion limits and immutable redemption records.
- Payments: centralized state machine, immutable status history, COD adapter and signed/idempotent webhook processing.
- Inventory: current balance plus immutable stock movement ledger.
- Reports: bounded UTC order/payment cohorts, cash flow, delivered-product ranking and low-stock snapshot.
- Notifications: transactional outbox, retry/dead-letter dispatch and configurable SMTP sender.

Public application service interfaces remain stable facades for controllers and hosted workers.
The large auth, order and operations implementations are composed from focused use cases:
registration/session/password reset, checkout/order queries/lifecycle commands, and
dead-letter/audit/retention operations. Facades do not own `DbContext` or transaction dependencies.

Repositories are feature-specific rather than generic. Their contracts expose business-oriented
queries and persistence operations without leaking `DbSet` or `IQueryable`. Application services
retain transaction orchestration and call `IUnitOfWork` at the same commit points as the business
use case; all repositories in one request share the same scoped `AppDbContext`.

## Checkout Flow

```text
Idempotency-Key lookup
  -> lock cart
  -> repeat idempotency lookup
  -> lock products in stable ID order
  -> lock promotion and recheck global/customer limits
  -> validate active products, price, stock and server-side quote
  -> create Pending Order + OrderDetails snapshots + Payment + StatusHistory
  -> snapshot promotion, shipping method and all monetary components
  -> increment promotion usage + append PromotionRedemption
  -> set a bounded inventory hold expiration
  -> reserve stock + append InventoryTransactions
  -> clear cart
  -> one SaveChanges + commit
```

The same user and idempotency key return the original order. Reusing that key with a
different address, note, payment method, shipping method or promotion returns `409 Conflict`.
`POST /api/orders/quote` is informational and expires after the configured interval. Checkout
never trusts a total from the client: it recalculates the quote after locking the relevant rows.
Promotion usage is consumed when the order is committed and is not restored by cancellation.
Clients may send the optional `ExpectedTotalAmount` from the latest quote. Checkout returns
`409 checkout_price_changed` before mutating state when the authoritative total no longer matches.
The free-standard-shipping threshold and configured tax rate apply to merchandise subtotal after
discount; shipping itself is not included in the taxable amount.

## Order And Payment State

```text
Order: Pending -> Confirmed -> Shipping -> Delivered -> Returned
         |           |           |
         +-----------+           +-> DeliveryFailed -> Shipping
                                      |
                                      +-> Cancelled

Payment: Pending -> Paid -> Refunded
            |------> Failed
            +------> Cancelled

COD reaches Paid when the order is Delivered and Cancelled when the order is Cancelled.
```

Stock is reserved while an order is Pending. Repeated updates to the current order status are
idempotent. Cancellation and return acceptance restore stock with distinct, unique inventory
movements. A delivery failure keeps stock reserved; staff can retry shipping or cancel fulfillment.

`POST /api/orders/{id}/refund` records a completed offline COD refund only after the order is
`Returned` and the payment is `Paid`. The request requires the external receipt/reference. Replaying
the same reference is idempotent; a different reference returns `409` instead of rewriting financial
history. Return acceptance and refund recording are intentionally separate because receiving the
item does not prove that money has already been returned.

Pending COD orders expire after the configured hold period. The expiration worker selects a bounded
batch by `(Status, ExpiresAt, Id)`, then locks each order and rechecks its state inside a transaction.
Expiration is represented as `Cancelled` with `CancellationReason=SystemExpired` and `ExpiredAt` so
existing API consumers do not need a new enum value. Customer cancellation is limited to an owned
`Pending` order. Checkout serializes on the customer cart and rejects creation above the configured
pending-order limit.

## Payment Webhooks

`GET /api/payments/methods` is the public capability contract for checkout clients. It lists only
methods that have a registered checkout provider; webhook-only adapters are excluded. Checkout
still resolves the selected method server-side and rejects an unregistered provider, so publishing
a new enum value alone cannot enable an incomplete payment path.

`POST /api/payments/webhooks/{providerCode}` reads a bounded, strict UTF-8 raw body. The generic
HMAC adapter verifies `HMAC_SHA256(secret, eventId + "." + rawBody)` from
`X-Payment-Signature`; `X-Payment-Event-Id` is unique per provider. Reusing an event ID with
different content returns `409`. A replay returns the result stored for the original event,
even if the payment has since moved to another state.

Webhook processing retains the SHA-256 payload hash by default, not the raw body. Set
`PaymentWebhooks:GenericHmac:RetainRawPayload=true` only for a time-bounded investigation after
confirming that the provider payload contains no data that should be minimized.

Payment transitions are `Pending -> Paid/Failed/Cancelled` and `Paid -> Refunded`. Every
accepted event is audited; a new event that leaves the payment in the same state does not add
another status-history row or notification. The generic adapter processes provider payments
that already have a provider transaction ID. It does not create a remote checkout session;
remote gateway I/O must be orchestrated after the inventory transaction commits.

`paid` and `refunded` events must include `amount`, and it must exactly match the expected
payment amount. Provider occurrence timestamps cannot predate payment creation, refunds cannot
predate payment capture, and timestamps beyond the configured future-clock tolerance are rejected.
Order lifecycle updates and payment webhooks both lock `Order -> Payment`; cancellation then locks
products in stable GUID order. This prevents cancellation and capture from committing an invalid
`Cancelled` order with a `Paid` payment.

## Transactional Outbox

Order placement, order status changes and payment webhooks append notification messages in
the same database transaction as business data. The background dispatcher atomically claims
messages, retries with exponential backoff and dead-letters after the configured attempt count.
Delivery is at-least-once; notification adapters receive the outbox ID as an idempotency key.
Enqueue, lease, completion, retry and backlog-health timestamps use the injected UTC clock.
SMTP messages also use a deterministic RFC `Message-ID` derived from that outbox ID, so every
retry of the same message carries the same delivery identity. This gives downstream mail systems
a stable deduplication signal, but does not claim exactly-once delivery: a process can stop after
SMTP accepts a message and before the database records completion. After the lease expires, the
dispatcher intentionally delivers that message again with the same `Message-ID`.

The project does not persist a separate provider-delivery receipt because SMTP does not expose a
portable idempotent acknowledgement contract. Provider-specific delivery tracking should only be
introduced with an email API whose contract and operational requirements can enforce it.

When `Outbox:RequireProcessing=true`, readiness also requires a recent dispatcher heartbeat. This
detects a stopped dispatcher before its backlog reaches the age threshold.

Admins can inspect dead letters without receiving their payload and re-drive one message at a
time. Re-drive locks the message, rechecks terminal state, resets retry state and appends an audit
event in one transaction. Concurrent or repeated requests are idempotent.

## Operations And Audit

Privileged role, catalogue, product-image and order-status mutations append an `AuditEvent` before
their transaction commits. Events contain bounded metadata, actor, forwarded client IP and the
request correlation ID; secrets and request payloads are excluded. Admin-only operations endpoints
provide paged audit/dead-letter reads and bounded upload reconciliation.

Upload reconciliation compares `/uploads/products` with `ProductImages`. Dry-run is the default.
Cleanup only touches application-generated orphan names older than the configured grace period;
missing referenced files are reported and never removed from the database automatically.

## Consistency Rules

- Cart mutations serialize per cart.
- Order lifecycle and webhook mutations lock the order before its payment.
- Checkout and cancellation lock product rows in stable GUID order.
- Checkout preflights every cart line after acquiring product locks and before mutating any stock,
  order, payment or cart state; an unavailable line therefore leaves the entire cart unchanged.
- Catalogue writes lock categories before products; multi-category updates lock category GUIDs in ascending order.
- Product-image mutations use the product row as their serialization boundary and load image state after acquiring that lock.
- Product administration writes stock through `InventoryPolicy`; the returned mutation is persisted verbatim in the inventory ledger.
- Product, category, order, payment, user and refresh-token rows use row-version concurrency.
- Category uniqueness is enforced both in code and filtered SQL unique indexes.
- Historical order names and prices come from `OrderDetails`, not the current product row.
- Every stock change caused by product administration or an order appends an inventory entry.
- Database constraints prevent duplicate order lines, payment outcomes and order inventory movements.
  Order lifecycle writes serialize on the locked order row so repeated delivery attempts can retain
  multiple `Shipping` and `DeliveryFailed` history entries.
- Payment and webhook status outcomes are persisted as immutable audit data with valid state/value constraints.

## Reporting Semantics

`GET /api/reports/sales-summary` uses the half-open UTC range `[From, To)` and limits a
request to 366 days. `TotalOrders` and `OrdersByStatus` are cohorts of orders created in the
range. `DeliveredOrders`, `CancelledOrders` and top products use the matching
`OrderStatusHistory` transition time. Gross cash collected uses `PaidAt`; refunds use the
`Refunded` payment history occurrence time; net revenue is gross collected minus refunds in the
range.

When the caller omits report dates, the service captures the injected UTC clock once and uses
the deterministic window `[now - 30 days, now)`. Invalid ranges, excessive ranges, low-stock
thresholds and top-product limits expose stable business error codes. SQL Server integration
tests lock the inclusion of `From`, exclusion of `To`, refund occurrence semantics and historical
product-name snapshots.

Top products include only orders delivered in the range, aggregate once per `ProductId`, and use
the latest historical name snapshot in that cohort. Low-stock count is a current inventory snapshot
using the requested threshold, not a historical value.
Top-product revenue is gross merchandise value from `OrderDetails` snapshots; order-level
discounts, shipping fees and taxes are intentionally not allocated across individual lines.

## Authorization

JWT roles are informational; protected administration endpoints require permission claims.
Access-token validation also verifies the user token version and an active refresh-token
family in SQL Server. Password and role changes revoke all existing sessions immediately.

A user account is the serialization boundary for session mutations. Login, refresh, logout,
logout-all, password changes and role changes lock the user before touching refresh tokens;
the fixed `User -> RefreshToken` lock order prevents a concurrent refresh from surviving a
session revocation. Refresh-token rotation and revocation are domain methods with private
mutation setters, and token activity is evaluated against an explicit UTC timestamp.

Identity services and the admin bootstrapper use the injected `TimeProvider`. Token creation,
rotation, family revocation, password changes and JWT expiry therefore share deterministic
security timestamps. Identity conflicts expose stable error codes while preserving the
existing HTTP 400, 401 and 409 status contracts.

Login performs a BCrypt verification for both known and unknown user names, returns the same
unauthorized contract and applies a configurable, automatically expiring lockout after repeated
failures. Password-reset requests intentionally return the same success response for registered
and unknown email addresses. Reset tokens are random, stored only as SHA-256 hashes, expire,
are single-use and are serialized per user. Completing a reset increments the user token version
and revokes every refresh-token family.

Password-reset notifications use a Data Protection protected outbox payload so the raw reset
token is not stored as readable JSON. Email verification is not an enforced sign-in condition:
the existing user schema has no verified-email lifecycle, and enabling it without enrollment and
migration rules would lock out existing accounts and break current clients. It should be introduced
only as a separately versioned product requirement.

## Operations

Every request receives an `X-Correlation-ID` response header. A caller-provided ID is accepted
only when it is 1-128 ASCII letters, digits, dots, underscores or hyphens; otherwise the server
uses the current activity trace ID or generates one. The same value becomes `traceId` in error
responses and a Serilog property in request, console and rolling-file logs. Activity IDs use W3C
format; `TraceId` and `SpanId` are separate structured log properties for cross-service tracing.

All responses also carry `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
`Referrer-Policy: no-referrer` and a restrictive permissions policy. These headers are added before
static product images are served as well as before API middleware runs.

Catalogue reads pass request cancellation into EF Core and split collection includes to avoid a
cartesian product when products have multiple images. Query count, duration and returned item
count are emitted without recording search text. The bounded `catalog.outcome` tag distinguishes
successful, cancelled and failed database queries so optimization decisions include unsuccessful
traffic instead of measuring only the happy path. Full-text search and response caching remain
measurement-gated because their operational cost and invalidation rules are not yet justified.

Exception, MVC validation, authentication, authorization and rate-limit failures share the
`application/problem+json` contract. Standard `ProblemDetails` fields coexist with the stable
compatibility fields `message`, `code`, `traceId`, `details` and `errors`.

Client-aborted requests are recorded as cancellation instead of an internal server error and do
not attempt to write JSON to a closed connection. Exceptions raised after response headers start
are rethrown because replacing a partially written response would corrupt the HTTP contract.

## Deliberate Boundaries

- Local image storage is retained behind `IUploadService` for the current deployment scope.
- Static serving is limited to generated product images under `/uploads/products`; only JPG, PNG
  and WEBP content types are exposed and responses disable MIME sniffing.
- SQL command timeout is configured through `Database:CommandTimeoutSeconds`. EF Core retry is not
  enabled globally because checkout, order lifecycle and webhooks own explicit transactions and
  locks; retries must wrap each complete business operation before they are introduced.
- COD is the only checkout method currently enabled. The generic HMAC webhook is provider
  neutral; a real gateway still needs its own Infrastructure adapter and credential mapping.
- Discount, shipping fee and tax remain zero at checkout because this project has no approved
  promotion, carrier or tax rules. Their persisted order snapshot columns are retained for a future
  calculator without changing historical order totals.
- Payment adapters are validated before persistence: provider codes must be route-safe, checkout
  methods must be defined, initial states must follow the payment state machine, and webhook-capable
  checkout providers must return a bounded transaction ID.
- Product variants, promotions, tax and shipping calculators remain future domain modules;
  they should only be added when their business rules are defined.
