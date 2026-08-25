# Mốc đo hiệu năng

Baseline này giúp các quyết định performance có thể đo và lặp lại. Đây không phải production
capacity forecast vì test host và SQL Server chạy trên cùng máy, không có network latency đại diện.

## Bằng chứng cho index catalog

Default catalog query được đo với 20.000 product đại diện trên SQL Server LocalDB trước và sau khi
thêm `IX_Products_IsDeleted_CreatedAt_Id`.

| Metric | Trước | Sau | Thay đổi |
| --- | ---: | ---: | ---: |
| `Products` logical read | 2.015 | 165 | -91,8% |
| SQL elapsed time | 37 ms | 2 ms | -94,6% |

Migration thêm `(IsDeleted ASC, CreatedAt DESC, Id DESC)`, khớp default product filter và stable
sort. Performance test cũng xác minh estimated plan của SQL Server tham chiếu index này.

## Rà soát query và phân trang

- Mọi public list chuẩn hóa paging và giới hạn `pageSize` ở 100.
- Catalog và order read dùng `AsNoTracking`; collection graph dùng split query để tránh cartesian
  row multiplication.
- Inventory, audit, dead-letter và reporting read project thẳng sang response model, không
  materialize writable entity.
- Order list endpoint hiện có giữ đầy đủ graph `OrderResponse` để tương thích. Product/order summary
  endpoint cung cấp SQL projection có giới hạn cho list-only client mà không đổi contract v1.
- Không thêm index hoặc migration trong lần review API v1. Default catalog index đã đo, order
  lifecycle index và retention index đã khớp hot query hiện tại; index mới cần query-plan hoặc
  telemetry evidence.

## Ngưỡng kiểm thử tự động

`SqlServerPerformanceTests` tạo SQL Server database cô lập, áp dụng toàn bộ migration và seed các
shape đại diện: 20.000 product, 100 product nhiều ảnh với 20 ảnh mỗi product, 5.000 historical order
cho một Customer và cart 50 dòng. Mỗi luồng được warm trước khi đo:

| Luồng | Workload | Budget mặc định |
| --- | --- | ---: |
| Catalog | 40 request, concurrency 8 | p95 <= 500 ms |
| Keyword catalog summary | 20 request, concurrency 4 | p95 <= 750 ms |
| Image-heavy catalog summary | 20 request, concurrency 4 | p95 <= 750 ms |
| Customer order-history summary | 20 request, concurrency 4 | p95 <= 750 ms |
| Admin dashboard summary | 20 request, concurrency 4 | p95 <= 1.000 ms |
| Revenue report | 20 request, concurrency 4 | p95 <= 1.500 ms |
| Login, tài khoản độc lập | 20 request, concurrency 4 | p95 <= 1.000 ms, >= 5 req/s |
| Refresh, token độc lập | 20 request, concurrency 4 | p95 <= 1.000 ms, >= 5 req/s |
| Session validation | 200 request, concurrency 16 | p95 <= 500 ms, >= 20 req/s |
| Checkout COD 50 dòng | 12 checkout độc lập, concurrency 12 | p95 <= 2.000 ms, >= 3 req/s |

Baseline checkout một dòng trước đây đo catalog p95 từ `53,3 ms` đến `76,9 ms`, session validation
từ `9,9 ms` đến `11,2 ms` và checkout từ `23,8 ms` đến `39,1 ms`. Các số liệu này chỉ được giữ làm
bằng chứng lịch sử và không thể so sánh trực tiếp với workload checkout 50 dòng hiện tại. Threshold
chủ ý rộng hơn một máy developer và có thể override bằng environment variable `PERFORMANCE_*`
tương ứng.

Lần chạy LocalDB đầu tiên với shape đại diện đo catalog p95 `32,1 ms`, keyword summary `344,3 ms`,
image-heavy summary `79,7 ms`, order-history summary `42,8 ms`, session validation `17,4 ms` và
checkout 50 dòng `179,9 ms`. Các giá trị thiết lập regression baseline cho cùng local workload;
chúng không phải production latency hoặc capacity claim.

Lần xác minh cuối trên Windows SQL Server ngày 2026-08-06 đo catalog p95 `37,5 ms`, keyword summary
`406,5 ms`, image-heavy summary `128,8 ms`, order-history summary `38,6 ms`, session validation
`13,4 ms` và checkout 50 dòng `228,2 ms`. Mọi luồng nằm trong budget cấu hình. Đây tiếp tục là local
regression sample, không phải production capacity estimate.

## Mốc Docker local ngày 2026-08-20

Lần chạy gần nhất dùng .NET `8.0.25` trên Windows `10.0.22621` với 12 logical processor và SQL
Server 2022 trong local Docker container. Dataset gồm 20.000 product, 2.000 product image, 5.000
historical order và 50 line độc lập mỗi checkout. API cùng SQL Server dùng chung một developer
machine nên kết quả không có network, ingress và production resource contention đại diện.

| Luồng | Concurrency | p50 | p95 | p99 | Throughput |
| --- | ---: | ---: | ---: | ---: | ---: |
| Catalog | 8 | 31,8 ms | 44,2 ms | 52,6 ms | 213,6 req/s |
| Keyword catalog summary | 4 | 239,3 ms | 265,6 ms | 266,0 ms | 16,4 req/s |
| Image-heavy catalog summary | 4 | 54,5 ms | 72,1 ms | 77,0 ms | 65,2 req/s |
| Customer order-history summary | 4 | 17,2 ms | 30,7 ms | 33,9 ms | 188,3 req/s |
| Admin dashboard summary | 4 | 39,8 ms | 64,7 ms | 64,7 ms | 92,2 req/s |
| Revenue report | 4 | 23,5 ms | 28,1 ms | 29,0 ms | 160,4 req/s |
| Login | 4 | 203,0 ms | 320,9 ms | 324,1 ms | 17,7 req/s |
| Refresh | 4 | 11,7 ms | 16,0 ms | 16,1 ms | 302,9 req/s |
| Session validation | 16 | 5,2 ms | 16,0 ms | 61,5 ms | 1.787,1 req/s |
| Checkout COD 50 dòng | 12 | 327,9 ms | 366,0 ms | 366,0 ms | 32,7 req/s |

Mọi regression budget cấu hình đều đạt. Login và refresh dùng account/token độc lập; performance
factory chỉ tăng auth/refresh permit limit local để không đo HTTP rate-limiter rejection. Hành vi
rate limit production vẫn được functional test bao phủ. Đây là single-run local regression value,
không phải load test, capacity forecast hoặc SLA.

Mọi measured request đều thành công nên observed application error rate là `0%`. Harness không thu
CPU utilization, process memory hoặc per-query SQL duration; environment metadata và
latency/throughput ở trên không được dùng để suy diễn các giá trị đó.

Chạy suite bằng `scripts/RunPerformanceTests.ps1`. GitHub workflow theo tuần/thủ công upload
`performance-results.json` để so sánh.

## Quyết định mở rộng

- Giữ session validation trên SQL Server khi p95 `auth.session.validation.duration` còn trong budget
  `500 ms` và SQL wait statistic chưa cho thấy đây là database load đáng kể.
  `auth.session.validations` cung cấp outcome volume có giới hạn mà không gắn user, session hoặc
  token tag. Chỉ cân nhắc Redis sau khi budget breach kéo dài được tái hiện dưới representative
  load; thay đổi đó phải bao gồm cache invalidation, revocation consistency và cache-unavailable
  behavior.
- Giữ SQL-backed catalog search khi keyword p95 còn trong budget `750 ms` và yêu cầu chỉ là keyword
  filtering có giới hạn. Review query plan và SQL full-text search trước. Chỉ thêm search engine khi
  latency đo được vẫn vượt budget hoặc yêu cầu sản phẩm cần relevance ranking, typo tolerance hay
  language-aware tokenization.
- Giữ SQL Outbox và hosted processor khi `outbox.backlog.pending` ổn định và
  `outbox.backlog.oldest_age` dưới `Outbox:MaxPendingAgeMinutes`. Điều tra provider và worker failure
  trước khi đổi kiến trúc. Chỉ cân nhắc worker hoặc broker riêng khi backlog tăng kéo dài, cần scale
  consumer độc lập hoặc xuất hiện yêu cầu fan-out mới.
- Giữ in-process rate limiter và local image storage cho một API instance hiện tại. Trước khi thêm
  replica thứ hai, cần distributed limiter và object storage hoặc shared durable volume đã test.

Outbox readiness check còn phát `outbox.backlog.dead_lettered`. Ba backlog metric được sample khi
`/health/ready`, `/health` hoặc `/health/details` chạy và có thể export bằng optional OTLP config
hiện có mà không thêm telemetry stack khác.

Các quyết định này cần được đánh giá lại bằng production telemetry, database wait statistic và
representative network load trước khi thêm infrastructure.
