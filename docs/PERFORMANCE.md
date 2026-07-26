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

## Automated Budgets

`SqlServerPerformanceTests` creates an isolated SQL Server database, applies all migrations,
seeds 20,000 products, warms each path and measures:

| Path | Workload | Default budget |
|---|---|---:|
| Catalog | 40 requests, concurrency 8 | p95 <= 500 ms |
| Session validation | 200 requests, concurrency 16 | p95 <= 500 ms, >= 20 req/s |
| COD checkout | 12 independent checkouts, concurrency 12 | p95 <= 2,000 ms, >= 3 req/s |

The first accepted LocalDB run measured catalog p95 `53.3 ms`, session validation p95 `9.9 ms`
and checkout p95 `23.8 ms`. Thresholds are intentionally wider than one developer machine and
can be overridden with the `PERFORMANCE_*` environment variables.

Run the suite with `scripts/RunPerformanceTests.ps1`. The weekly/manual GitHub workflow uploads
`performance-results.json` for comparison.

## Scale Decisions

- Keep session validation on SQL Server. The current indexed lookup is comfortably inside the
  baseline; Redis would add cache invalidation and availability failure modes without measured
  benefit.
- Keep the existing in-process rate limiter for the current single API instance. A distributed
  limiter becomes necessary only when multiple API replicas are deployed.
- Keep product images on local storage for the current single-instance scope. Move to object
  storage before horizontal API scaling or when backup/storage measurements require it.

Revisit these decisions using production telemetry, database wait statistics and representative
network load before adding infrastructure.
