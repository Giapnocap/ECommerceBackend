# Database Design

## Core Relationships

```text
Users 1--1 Carts 1--* CartItems *--1 Products
Users 1--* Orders 1--* OrderDetails *--1 Products
Users 1--* RefreshTokens
Orders 1--1 Payments
Payments 1--* PaymentWebhookEvents
Payments 1--* PaymentStatusHistories
Orders 1--* OrderStatusHistories
Orders 1--* InventoryTransactions *--1 Products
OutboxMessages (transactional notification queue)
AuditEvents (append-only privileged action trail)
Categories 1--* Categories
Categories 1--* Products 1--* ProductImages
Users *--* Roles *--* Permissions
```

## Historical Data

`Orders` stores address and monetary totals at checkout time. `OrderDetails` stores product
name, price and quantity snapshots. Product edits or soft deletion therefore do not rewrite
past orders.

`OrderStatusHistories` and `InventoryTransactions` are append-only business ledgers. The
current status and stock balance stay on `Orders` and `Products` for efficient reads.

`Orders.ExpiresAt` records the inventory hold deadline. `CancelledAt`, `ExpiredAt` and
`CancellationReason` distinguish customer/staff cancellation from automatic expiration. Legacy
cancelled rows are backfilled during migration; nullable columns keep rolling deployment compatible.

`PaymentWebhookEvents` stores the provider event ID, raw-payload hash, provider occurrence time and immutable processing result. `PaymentStatusHistories` records checkout, order-lifecycle, webhook and legacy-backfill transitions.
`OutboxMessages` stores notifications written in the same transaction as order/payment data,
including claim, retry and dead-letter state.

`AuditEvents` records actor, action, entity identity, correlation ID, client IP and bounded metadata.
Privileged services append the audit row before their existing `SaveChanges`, so the business
mutation and audit evidence commit or roll back together. Audit rows have no cascade relationship
to users and remain readable after account lifecycle changes.

## Important Constraints

- Unique normalized username and email.
- One cart per user and one cart line per product.
- One active main image per product through a filtered unique index.
- Unique category name within its parent for active categories.
- Unique order number and (UserId, IdempotencyKey); one product line per order.
- Pending-order and expiration indexes support per-customer limits and bounded worker batches.
- One history entry per reached order status.
- One reserve/release inventory movement per order and product; movement sign and order link must match its type.
- One payment per order and unique `(Provider, ProviderTransactionId)` when present.
- Unique (Provider, ProviderEventId) for webhook replay protection.
- One payment-history row per reached status; source/status ranges and actual status changes are constrained.
- Payment PaidAt must be present only for Paid/Refunded; webhook result and SHA-256 hash shape are constrained.
- Outbox attempt counts cannot be negative and lock ID/timestamp must be set or cleared together.
- Audit action, entity type and correlation ID are required; indexes support actor, entity,
  correlation and reverse-chronological investigation queries.
- Positive prices/order totals; non-negative stock and ledger balances.
- Decimal money columns use decimal(18,2).
- Reporting indexes cover order date, payment status/creation, paid time, refund occurrence and active stock quantity.

## Migration Policy

- Never edit a migration that has already been deployed to a shared environment.
- Back up production before applying a migration.
- Backfill new required columns before creating unique indexes or check constraints.
- Seed stable roles and permissions only. Administrator credentials are bootstrapped from
  runtime secrets and are never stored in a production migration.
- Validate a clean migration and an upgrade migration against a dedicated SQL Server test
  database before release.
