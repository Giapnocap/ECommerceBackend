# System Boundaries

This is a portfolio-ready backend release, not a claim of unlimited production scale. The
following boundaries are intentional and visible in the design.

## Current Scope

- Checkout supports cash on delivery only. The generic signed webhook demonstrates a provider
  contract but is not a complete integration with a real payment gateway.
- Checkout supports configurable shipping/tax rules and bounded promotion codes. It does not
  calculate carrier-specific live rates, stack multiple promotions or model jurisdictional tax.
- Shipment records and return processing are internal workflows. Carrier label creation, live
  tracking synchronization, product variants and multi-warehouse inventory are outside the domain.
- Email verification is not required for sign-in. Password reset, lockout and session revocation
  are implemented.

## Deployment Boundaries

- One API instance and one SQL Server database are the supported topology.
- Product images use local disk. Horizontal API scaling requires object storage or a shared
  durable volume.
- Rate limiting is in process. Multiple API replicas require a distributed limiter.
- Session validation reads SQL Server on protected requests. Current load tests do not justify
  adding Redis.
- SMTP delivery is at-least-once. A crash after SMTP accepts a message but before the database
  commit can send a duplicate with the same deterministic `Message-ID`.

## Operational Boundaries

- CI configuration, packaging, migration rollback and backup/restore drills are implemented.
  Actual cloud deployment, DNS, certificates and managed-secret integration depend on the target
  environment and are not claimed by this repository.
- `rollback-last.sql` is suitable only while the last migration remains data-compatible. Restore
  the verified database backup when a migration has transformed or removed production data.
- Performance numbers are regression baselines from a local/CI topology, not production capacity
  estimates. Re-evaluate indexes and infrastructure with production telemetry and network latency.

## Compatibility

- OpenAPI v1 is snapshot-tested. `/api/v1` is the canonical route and `/api` remains a
  backward-compatible alias that assumes v1.
- A breaking request or response change requires a new API version; the v1 DTO and error-code
  contracts remain stable.
- Order and payment status names are public API values; renaming them is backward incompatible.
- Historical order details and immutable ledgers must not be reconstructed from current catalog
  values.
