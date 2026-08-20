# Performance Baseline

This baseline keeps performance decisions measurable and repeatable. It is not a production
capacity forecast because the test host and SQL Server run on the same machine without network
latency.

## Catalog Index Evidence

The default catalog query was measured against 20,000 representative products on SQL Server
LocalDB before and after adding `IX_Products_IsDeleted_CreatedAt_Id`.

| Metric | Before | After | Change |
|---|---:|---:|---:|
| `Products` logical reads | 2,015 | 165 | -91.8% |
| SQL elapsed time | 37 ms | 2 ms | -94.6% |

The migration adds `(IsDeleted ASC, CreatedAt DESC, Id DESC)`, matching the default product
filter and stable sort. The performance test also verifies that SQL Server's estimated plan
references this index.

## Query And Paging Review

- Every public list normalizes paging and limits `pageSize` to 100.
- Catalog and order reads use `AsNoTracking`; collection graphs use split queries to avoid
  cartesian row multiplication.
- Inventory, audit, dead-letter and reporting reads project directly to response models and do
  not materialize writable entities.
- Existing order list endpoints retain the full `OrderResponse` graph for compatibility. Product
  and order summary endpoints provide bounded SQL projections for list-only clients without
  changing those version 1 contracts.
- No index or migration was added during the API v1 review. The measured default catalog index,
  order lifecycle indexes and retention indexes already match the current hot queries; additional
  indexes require query-plan or telemetry evidence.

## Automated Budgets

`SqlServerPerformanceTests` creates an isolated SQL Server database, applies all migrations and
seeds representative shapes: 20,000 products, 100 image-heavy products with 20 images each,
5,000 historical orders for one customer and 50-line carts. Every path is warmed before measuring:

| Path | Workload | Default budget |
|---|---|---:|
| Catalog | 40 requests, concurrency 8 | p95 <= 500 ms |
| Keyword catalog summary | 20 requests, concurrency 4 | p95 <= 750 ms |
| Image-heavy catalog summary | 20 requests, concurrency 4 | p95 <= 750 ms |
| Customer order-history summary | 20 requests, concurrency 4 | p95 <= 750 ms |
| Admin dashboard summary | 20 requests, concurrency 4 | p95 <= 1,000 ms |
| Revenue report | 20 requests, concurrency 4 | p95 <= 1,500 ms |
| Login, tài khoản độc lập | 20 requests, concurrency 4 | p95 <= 1,000 ms, >= 5 req/s |
| Refresh, token độc lập | 20 requests, concurrency 4 | p95 <= 1,000 ms, >= 5 req/s |
| Session validation | 200 requests, concurrency 16 | p95 <= 500 ms, >= 20 req/s |
| 50-line COD checkout | 12 independent checkouts, concurrency 12 | p95 <= 2,000 ms, >= 3 req/s |

Earlier one-line checkout baselines measured catalog p95 between `53.3 ms` and `76.9 ms`, session
validation between `9.9 ms` and `11.2 ms`, and checkout between `23.8 ms` and `39.1 ms`. They are
retained only as historical evidence and are not directly comparable with the current 50-line
checkout workload. Thresholds are intentionally wider than one developer machine and can be
overridden with the matching `PERFORMANCE_*` environment variables.

The first representative-shape LocalDB run measured catalog p95 `32.1 ms`, keyword summary
`344.3 ms`, image-heavy summary `79.7 ms`, order-history summary `42.8 ms`, session validation
`17.4 ms` and 50-line checkout `179.9 ms`. These values establish a regression baseline for the
same local workload; they are not production latency or capacity claims.

The final Windows SQL Server verification on 2026-08-06 measured catalog p95 `37.5 ms`, keyword
summary `406.5 ms`, image-heavy summary `128.8 ms`, order-history summary `38.6 ms`, session
validation `13.4 ms` and 50-line checkout `228.2 ms`. Every path remained within its configured
budget. This is another local regression sample, not a production capacity estimate.

## Local Docker Baseline 2026-08-20

The latest run used .NET `8.0.25` on Windows `10.0.22621` with 12 logical processors and SQL Server
2022 in a local Docker container. The dataset contained 20,000 products, 2,000 product images,
5,000 historical orders and 50 independent lines per checkout. API and SQL Server shared one
developer machine, so the result excludes representative network, ingress and production resource
contention.

| Path | Concurrency | p50 | p95 | p99 | Throughput |
|---|---:|---:|---:|---:|---:|
| Catalog | 8 | 31.8 ms | 44.2 ms | 52.6 ms | 213.6 req/s |
| Keyword catalog summary | 4 | 239.3 ms | 265.6 ms | 266.0 ms | 16.4 req/s |
| Image-heavy catalog summary | 4 | 54.5 ms | 72.1 ms | 77.0 ms | 65.2 req/s |
| Customer order-history summary | 4 | 17.2 ms | 30.7 ms | 33.9 ms | 188.3 req/s |
| Admin dashboard summary | 4 | 39.8 ms | 64.7 ms | 64.7 ms | 92.2 req/s |
| Revenue report | 4 | 23.5 ms | 28.1 ms | 29.0 ms | 160.4 req/s |
| Login | 4 | 203.0 ms | 320.9 ms | 324.1 ms | 17.7 req/s |
| Refresh | 4 | 11.7 ms | 16.0 ms | 16.1 ms | 302.9 req/s |
| Session validation | 16 | 5.2 ms | 16.0 ms | 61.5 ms | 1,787.1 req/s |
| 50-line COD checkout | 12 | 327.9 ms | 366.0 ms | 366.0 ms | 32.7 req/s |

All configured regression budgets passed. Login and refresh used independent accounts/tokens; the
performance factory raised only its local auth/refresh permit limits to avoid measuring HTTP rate
limiter rejection. Production rate-limit behavior remains covered by functional tests. These are
single-run local regression values, not a load test, capacity forecast or SLA.

All measured requests completed successfully, so the observed application error rate was `0%`.
CPU utilization, process memory and per-query SQL duration were not captured by this harness; the
environment metadata and latency/throughput figures above must not be used to infer those values.

Run the suite with `scripts/RunPerformanceTests.ps1`. The weekly/manual GitHub workflow uploads
`performance-results.json` for comparison.

## Scale Decisions

- Keep session validation on SQL Server while `auth.session.validation.duration` p95 remains within
  the `500 ms` budget and SQL wait statistics do not identify it as a material database load.
  `auth.session.validations` provides bounded outcome volume without user, session or token tags.
  Consider Redis only after a sustained budget breach is reproduced under representative load;
  include cache invalidation, revocation consistency and cache-unavailable behavior in that change.
- Keep SQL-backed catalog search while keyword p95 remains within the `750 ms` budget and the
  required behavior is bounded keyword filtering. Review the query plan and SQL full-text search
  first. Add a dedicated search engine only when measured latency remains over budget or product
  requirements need relevance ranking, typo tolerance or language-aware tokenization.
- Keep the SQL outbox and hosted processor while `outbox.backlog.pending` remains stable and
  `outbox.backlog.oldest_age` stays below `Outbox:MaxPendingAgeMinutes`. Investigate provider and
  worker failures before changing architecture. Consider a separate worker or broker only for a
  sustained growing backlog, independently scaled consumers or new fan-out delivery requirements.
- Keep the existing in-process rate limiter and local image storage for the current single API
  instance. Before adding a second replica, introduce a distributed limiter and object storage or
  a tested shared durable volume.

The outbox readiness check also emits `outbox.backlog.dead_lettered`. These three backlog metrics
are sampled whenever `/health/ready`, `/health` or `/health/details` runs and can be exported by
the existing optional OTLP configuration without adding another telemetry stack.

Revisit these decisions using production telemetry, database wait statistics and representative
network load before adding infrastructure.
