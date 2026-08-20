# ECommerceBackend Full Upgrade Report

Audit baseline: `2f6afd92b59c9dbeb9b16b486bafc39ec9b9fbb1`
Audit date: 2026-08-20
Target runtime: .NET 8, ASP.NET Core Web API, EF Core and SQL Server

## Purpose

This report describes capabilities implemented in the repository. It is not an operational
verification report and does not claim that Stripe, CurrencyAPI, SMTP, TLS or a staging host have
been tested with real external credentials. Those results belong in
`PRODUCTION_READINESS_REPORT.md`.

## Architecture

The solution separates the HTTP host, application use cases, domain rules and infrastructure:

```text
ECommerceBackend (API)
    -> ECommerceBackend.Application
        -> ECommerceBackend.Domain
    -> ECommerceBackend.Infrastructure
        -> Application + Domain
```

- Controllers own HTTP contracts, authorization metadata and response status codes.
- Application use cases orchestrate validation, transactions, repositories, provider ports,
  audit and outbox writes.
- Domain entities and policies protect status transitions, money, inventory and refund invariants.
- Infrastructure implements EF Core repositories, SQL locking, Stripe/CurrencyAPI/SMTP adapters,
  durable local image storage and hosted workers.
- Unit tests cover domain rules; integration tests cover the assembled API and adapters; tagged
  SQL Server tests cover relational constraints, locking, migrations, recovery and performance.

## Business Flows

### Authentication And Sessions

- Registration and login use DTO validation and BCrypt password hashing.
- Login performs equivalent BCrypt work for unknown users, supports lockout and returns a generic
  unauthorized contract.
- Access tokens contain session and token-version claims. Protected requests validate the active
  SQL-backed session.
- Refresh tokens are stored as SHA-256 hashes, rotate by family and trigger family revocation on
  reuse.
- Password reset and email verification use hashed, expiring, single-use tokens. Password reset
  increments the token version and revokes existing sessions.

### Catalog, Cart And Checkout

- Catalog writes enforce active-category uniqueness, soft deletion and optimistic concurrency.
- Cart writes serialize per cart and store a display snapshot; checkout always recalculates prices
  from authoritative product and promotion data.
- Checkout uses an idempotency key, locks the cart and products in stable order, snapshots recipient,
  product, promotion and money data, reserves stock, appends ledger/history/outbox records and
  commits once.
- Concurrent retry returns one logical order; conflicting reuse returns `409`; failed validation
  rolls the complete transaction back.

### Fulfillment, Return And Inventory

- Staff/Admin confirmation, shipment dispatch, delivery failure/retry and delivery are guarded by
  explicit order state transitions.
- Customers can request a return only for an owned, delivered order inside the configured window.
- Staff review and receipt are separate actions. Stock is restored exactly once only after approved
  goods are received.
- Every stock mutation appends an inventory transaction with the balance after mutation.
- Per-product low-stock thresholds support operational stock views and reports.

## Payment And Refund

- COD and Stripe card payment methods are exposed only when a complete provider is registered.
- `IPaymentGateway` isolates application logic from Stripe HTTP contracts.
- PaymentIntent creation uses an external-creation idempotency key and lease. Network I/O runs
  outside SQL transactions, then provider state is attached in a short transaction.
- Stripe webhooks verify signatures, timestamps, event identity, payment identity, amount and
  currency. Duplicate events do not repeat state transitions or side effects.
- The payment state machine supports `Pending`, `RequiresAction`, `Processing`, `Paid`, `Failed`,
  `Cancelled`, `PartiallyRefunded` and `Refunded`.
- The reconciliation worker selects a bounded batch of stale active payments, queries Stripe and
  locks each order/payment before applying only valid, idempotent transitions when a webhook is
  delayed or lost. The supported single-API topology does not require a distributed query lease.
- Online refund calls the original provider path and supports partial/full refunds. A
  `PaymentRefund` idempotency key, processing lease, row version and cumulative amount checks protect
  retries and concurrency.
- COD refund is an auditable manual recording after returned goods have been received.

## Money And Currency

- `Money` validates supported ISO codes, currency scale, rounding and overflow.
- VND is the reporting base currency; VND, USD and EUR are supported transaction currencies.
- Orders snapshot exchange rate, capture time and base/display totals; order lines snapshot base and
  display unit prices.
- Refunds preserve original payment currency and VND base amount. The final refund receives the
  remaining base amount to avoid cumulative rounding drift.
- CurrencyAPI integration has timeout, process-local cache, single-flight fetch and a bounded stale
  fallback. Reports aggregate base snapshots rather than mixing currencies.

## Reliability And Operations

- Transactional outbox messages commit with business data, use bounded retries, leases,
  dead-lettering and Admin redrive.
- SMTP uses a deterministic RFC `Message-ID`; delivery is explicitly at-least-once.
- Order expiration, payment reconciliation, outbox dispatch and data retention run as hosted
  services with health/status signals.
- Correlation IDs connect ProblemDetails, structured Serilog request logs, audit events and OpenTelemetry
  activities.
- Liveness, readiness and protected detailed-health endpoints cover process, SQL Server, storage and
  required workers.
- Docker Compose provides SQL migration ordering and persistent volumes for database files, uploads,
  data-protection keys and logs.

## Security

- JWT issuer, audience, signing key and lifetime are strongly validated at startup.
- Permission policies and resource-ownership checks protect privileged and customer-specific data.
- API errors use bounded ProblemDetails contracts and do not expose stack traces outside Development.
- Uploads validate extension, MIME type, file signature, size and generated path before persistence.
- Security headers, CORS allowlists, HSTS, HTTPS redirection and forwarded-header processing are
  configured at the API boundary.
- Secrets are expected from environment variables or an external secret store and are absent from
  committed runtime templates.
- Audit metadata and API output redact password, token, secret, API-key and credential fields.

## Management And Reporting

- Admin/Staff workflows cover products, categories, inventory, orders, shipments, returns and
  account management according to permission policies.
- Admin management includes customer lock/unlock, dashboard, revenue/order/product/customer/return
  reports, promotion analytics, audit search, outbox dead-letter redrive and upload reconciliation.
- Read models use database projections, `AsNoTracking`, bounded paging and base-currency aggregates.

## Testing Assets

The repository contains:

- domain unit tests for state machines, money and business invariants;
- API/application integration tests for auth, authorization, checkout, payment, refund, outbox,
  reporting and operations;
- deterministic Stripe gateway/webhook and CurrencyAPI adapter tests;
- SQL Server tests for locking, concurrency, constraints, migrations, backup/restore and performance;
- architecture, OpenAPI compatibility, deployment security and observability contract tests;
- CI workflows for restore audit, formatting, Release build, migrations, coverage and SQL tests;
- Docker smoke, migration artifact, recovery, performance and release packaging scripts.

Test presence is implementation evidence only. Current pass/fail counts, Docker results, external
provider checks and release recommendation are recorded separately after executing the final
readiness verification.

## Deliberate Boundaries

- Supported deployment topology is one API instance, one SQL Server and persistent product-image
  storage.
- Stripe, CurrencyAPI and SMTP are disabled until credentials are supplied externally.
- Email verification is recorded but is not required for login.
- Reconciliation repairs payments, not provider-pending refunds.
- Rate limiting and FX cache are process-local and are not presented as horizontally distributed.
- Local/CI performance results are regression baselines, not production capacity claims.
