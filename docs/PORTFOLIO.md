# Portfolio Evidence

## Project Summary

**E-Commerce Backend** is a personal ASP.NET Core 8 Web API built as a layered modular monolith
with Entity Framework Core and SQL Server. The project focuses on transaction safety, authorization,
concurrency, auditability and repeatable release verification rather than UI or infrastructure
breadth.

## Verified Engineering Work

- Implemented JWT access tokens, refresh-token family rotation, reuse detection, account lockout,
  password reset and immediate session revocation.
- Enforced role/permission authorization for Customer, Staff and Admin workflows.
- Built idempotent COD checkout with SQL transactions, stable lock ordering, historical order
  snapshots, optimistic concurrency and an immutable inventory ledger.
- Modeled delivery failure, retry, cancellation, return and offline refund as validated order and
  payment state transitions.
- Implemented signed/idempotent payment webhooks and a transactional outbox with retry,
  dead-lettering, redrive and deterministic message identity.
- Added structured logs, correlation/trace IDs, OpenTelemetry, health checks, audit events and
  consistent ProblemDetails responses.
- Added migration forward/rollback verification, SQL Server concurrency tests, backup/restore
  recovery drill, release checksums and a published-artifact smoke test.
- Established performance regression tests for catalog, session validation and checkout. The
  catalog index reduced representative logical reads from 2,015 to 165.

## Quality Evidence

- Release build: zero compiler warnings.
- Automated tests: 257 regular tests, 13 SQL Server integration tests, one recovery drill and one
  opt-in performance suite.
- Coverage baseline: 80.48% line and 64.07% branch coverage.
- API compatibility: committed OpenAPI snapshot with contract tests.
- Supply chain: NuGet vulnerability audit and tracked-file secret scan in CI.

## Suggested CV Entry

**E-Commerce Backend | C#, ASP.NET Core 8, EF Core, SQL Server**

- Developed a layered REST API with JWT/RBAC, catalog, cart, COD ordering, inventory, reporting
  and operational audit workflows.
- Designed idempotent checkout and order/payment state machines using SQL transactions, row locks,
  row-version concurrency and immutable history/ledger records.
- Implemented refresh-token rotation and revocation, signed webhook replay protection, and a
  transactional outbox with retry/dead-letter handling.
- Built automated quality gates covering API contracts, SQL Server race conditions,
  migration rollback, backup/restore recovery, coverage and release artifact checksums.
- Profiled the catalog query on 20,000 products and reduced SQL logical reads by 91.8% with a
  measured composite index.

## Claims To Avoid

Do not describe this project as microservices, Kubernetes, multi-region, exactly-once messaging,
online payment gateway integration or horizontally scalable storage. Docker, MySQL and cloud
deployment may be listed as separate learning experience only when supported by separate evidence.
