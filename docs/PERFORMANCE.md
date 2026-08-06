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
