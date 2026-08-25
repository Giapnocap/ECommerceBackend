# Báo cáo mức độ sẵn sàng của ECommerceBackend

## Thông tin baseline

| Mục | Giá trị |
| --- | --- |
| Commit code và test | `5af8feb2d307af39550b67e9124926a7a70b039c` trên branch `main` |
| CI baseline | [GitHub Actions run 32379122754](https://github.com/Giapnocap/ECommerceBackend/actions/runs/32379122754) của đúng commit `5af8feb` |
| Ngày audit | 2026-08-20 |
| Ngày chuẩn hóa tài liệu | 2026-08-25 |
| Môi trường local | Windows `10.0.22621`, .NET `8.0.25`, 12 logical processors |
| Cơ sở dữ liệu xác minh | SQL Server 2022 Linux container, Docker Engine `29.7.2` |

Mọi số test và coverage trong báo cáo này dùng cùng baseline code/test `5af8feb`. CI cung cấp kết
quả build, unit test, integration test, SQL Server test và coverage; performance baseline được chạy
riêng trên đúng source baseline. Các commit tài liệu sau baseline không thay đổi `src/`, `tests/`
hoặc workflow CI.

Các trạng thái dùng trong báo cáo: `VERIFIED`, `IMPLEMENTED_NOT_EXTERNAL_VERIFIED`,
`BLOCKED_EXTERNAL`, `FAILED`, `NOT_REQUIRED`.

## Trạng thái tổng quan

| Hạng mục | Trạng thái hiện tại | Mức xác minh | Bằng chứng | Khoảng trống | Hành động |
| --- | --- | --- | --- | --- | --- |
| Tài liệu | Kiến trúc, ERD, sequence, giới hạn, monitoring, demo và runbook đã được đối chiếu | VERIFIED | Rà soát Markdown, link, API và source | Chưa được operator ngoài dự án review | Review khi bàn giao staging |
| Release build | Release build không có warning hoặc error | VERIFIED | `dotnet build ... --configuration Release --no-restore` | Không | Giữ CI gate |
| Automated test | Unit, integration, SQL, recovery và performance test đạt | VERIFIED | 653 test pass trong các batch độc lập của cùng baseline | External provider E2E không thuộc deterministic suite | Chạy checklist external |
| Coverage | Line 82,88%, branch 66,30% | VERIFIED | `VerifyCoverage.ps1` với gate 80%/60% | Coverage không phải độ bảo đảm tuyệt đối | Theo dõi regression |
| Migration | Artifact, checksum, upgrade/rollback/upgrade và model drift đạt | VERIFIED | 25 SQL test; `has-pending-model-changes` sạch | Chưa chạy với staging data volume | Diễn tập backup và migration trên staging |
| Backup/restore | Latest schema và dữ liệu nghiệp vụ quan trọng phục hồi đúng | VERIFIED | 1/1 `SqlServerRecoveryIntegration` local | Chưa phải backup production | Thiết lập scheduler và restore drill trên staging |
| Docker | Build, migration ordering, non-root API, health và volume restart đạt | VERIFIED | Migration exit 0; live/ready 200; DB, upload, key và log còn nguyên sau restart | Chưa chạy trên deployment host | Chạy lại với staging registry/host |
| Bảo mật | Config fail-closed, RBAC test, dependency audit và secret scan đạt | VERIFIED | Security/config test; NuGet advisory và secret scan sạch | Chưa có pentest/DAST bên ngoài | Thực hiện theo risk profile |
| Khả năng quan sát | Structured log, correlation ID, OpenTelemetry và health checks đã có | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | 27 test; `docs/MONITORING.md` | Chưa nối collector, dashboard và alert thật | Cấu hình OTLP và kích hoạt alert trên staging |
| Stripe | Gateway, PaymentIntent và raw Webhook validation được test bằng adapter deterministic | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | Gateway, Webhook và Idempotency test đạt | Chưa có Stripe test credential/Webhook delivery | Chạy Stripe Test Mode E2E |
| Refund | Partial/full refund, cumulative cap, original currency và concurrency được bảo vệ | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | Refund test và SQL concurrency test đạt | Chưa gọi Stripe Test Mode thật | Chạy partial/full refund E2E |
| Reconciliation | Worker phục hồi missed Webhook và từ chối amount/currency mismatch | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | Reconciliation success/mismatch test đạt | Chưa đối chiếu Stripe Test Mode thật | Chạy missed-Webhook E2E |
| FX provider | Cache, single-flight, timeout, stale bound và USD/EUR snapshot được test | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | CurrencyAPI adapter test đạt | Chưa có API key/quota thật | Chạy CurrencyAPI trên staging |
| SMTP | Token lifecycle, Outbox và SMTP config/TLS validation đã có | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | Auth, Outbox và config test đạt | Chưa có SMTP credential và inbox thật | Gửi email verify/reset trên staging |
| Staging HTTPS | Template và validation cho host, CORS, proxy, TLS đã chuẩn bị | BLOCKED_EXTERNAL | `appsettings.Staging.example.json` và startup test | Chưa có host, DNS, TLS, trusted proxy | Provision staging và chạy smoke test |
| CI của release candidate | Ba job của Backend CI đạt | VERIFIED | Run `32379122754` cho commit `5af8feb` | Không | Duy trì CI gate sau mỗi push |
| Tag `v1.0.0` | Chưa tạo tag | BLOCKED_EXTERNAL | Chưa có đủ production/external verification | Các mục external còn block | Chỉ tạo tag sau khi gate bắt buộc đạt |

## Build, test và coverage

| Gate | Kết quả | Trạng thái |
| --- | ---: | --- |
| `dotnet format --verify-no-changes` | Sạch | VERIFIED |
| Release solution build | 0 warning, 0 error | VERIFIED |
| Unit test | 279/279 | VERIFIED |
| Integration/contract test không dùng SQL tag | 347/347 | VERIFIED |
| SQL Server integration | 25/25 | VERIFIED |
| SQL Server backup/restore | 1/1 | VERIFIED |
| SQL Server performance | 1/1 | VERIFIED |
| Tổng full gate | 653 pass, 0 fail, 0 skip | VERIFIED |
| Line/branch coverage | 82,88% / 66,30% | VERIFIED |
| Migration model drift | Không có | VERIFIED |
| Release package checksum, manifest và smoke test | Đạt | VERIFIED |

Các batch targeted dùng để định vị lỗi trong audit không được cộng lặp vào tổng 653. Performance
test chạy riêng, không nằm trong ba job của Backend CI.

## Cơ sở dữ liệu và khả năng phục hồi

- Latest migration: `20260818210000_AddRefundMoneySnapshots`.
- Rollback target trong artifact: `20260818200000_AddMoneySnapshots`.
- Idempotent forward script chạy được; rollback một migration rồi forward lại thành công.
- Migration snapshot người nhận từ chối rollback dữ liệu không thể bảo toàn thay vì âm thầm xóa.
- Backup/restore khôi phục latest schema cùng User, Order, OrderDetail, Payment, Product,
  InventoryTransaction, OutboxMessage, AuditEvent và snapshot VND/USD trên SQL Server thật.
- Docker SQL volume giữ dữ liệu qua container restart.

Trạng thái: `VERIFIED` với SQL Server local/cô lập. Backup scheduler, off-host copy, RPO/RTO và
restore drill trên staging/production là `BLOCKED_EXTERNAL`.

## Ma trận lỗi và tính nhất quán

| Tình huống | Trạng thái đầu | Hành động | Kết quả mong đợi | Kết quả thực tế | Invariant được bảo vệ | Bằng chứng | Trạng thái |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Checkout trùng | Một cart, một Idempotency key | Gửi hai request đồng thời | Một logical order | Cả hai request nhận cùng một order | Không trùng order hoặc stock mutation | `ConcurrentDuplicateCheckout_ReturnsOneCommittedOrder` | VERIFIED |
| Checkout đồng thời | Hai Customer tranh sản phẩm cuối | Checkout đồng thời | Chỉ một order commit, stock không âm | Một request thành công, một request bị từ chối an toàn | Non-negative stock, không lost update | `ConcurrentCustomers_CompetingForLastItem_CreateOneOrder` | VERIFIED |
| Điều chỉnh stock và checkout | Product có stock hữu hạn | Adjustment và checkout đồng thời | Ledger khớp stock cuối | Stock và ledger nhất quán | Inventory mutation được serialize | Hai SQL inventory concurrency test | VERIFIED |
| Tạo payment trùng | Card payment chưa có provider ID | Gọi initialize lặp | Cùng provider identity và Idempotency key | Local payment chỉ gắn một provider ID; HTTP ngoài Transaction | Không tạo payment kép | `ExternalCreation_IsIdempotentAndRunsOutsideDatabaseTransaction` | VERIFIED |
| Request dừng khi tạo payment | Provider trả PaymentIntent, local completion chưa commit | Hủy request rồi retry sau lease | Retry dùng cùng provider Idempotency key và hoàn tất một lần | Cùng key, một local provider ID, lease được xóa | Không tạo orphan payment trùng | `ExternalCreation_RequestInterruptedAfterProviderSuccess_RetriesWithSameIdempotencyKey` | VERIFIED |
| Webhook trùng hoặc bị sửa | Event đã xử lý | Replay cùng payload hoặc đổi payload | Replay không side effect; payload bị sửa bị từ chối | Đúng như mong đợi | Local effect chỉ xảy ra một lần | Payment Webhook replay/hash test | VERIFIED |
| Webhook sai amount/currency | Payment đang active | Gửi signed event sai amount/currency | Không mutation | Payment, history và Outbox không đổi | Payment/order money consistency | Hai mismatch test | VERIFIED |
| Outbox worker crash/restart | Message đã gửi nhưng completion chưa commit | Mô phỏng crash, hết lease rồi chạy worker mới | Message được reclaim với cùng identity | Redelivery cùng `Message-ID` rồi đánh dấu processed | Durable at-least-once delivery | `CrashAfterDelivery_ReclaimsLeaseAndRedeliversSameOutboxMessage` | VERIFIED |
| SQL concurrency conflict | Paid Webhook và cancellation tranh order/payment | Chạy hai Transaction đồng thời | Không tạo tổ hợp state sai | Order/payment invariant được giữ | Stable lock order và state consistency | `ConcurrentPaidWebhookAndCancellation_PreserveOrderPaymentInvariant` | VERIFIED |
| FX provider lỗi | Cache fresh, stale hoặc expired | Provider timeout/failure | Dùng stale trong giới hạn, ngoài giới hạn thì fail | Đúng như mong đợi | Snapshot lịch sử ổn định, không tự đặt rate | CurrencyAPI failure test | VERIFIED |
| Refund đồng thời | Paid payment còn refundable balance | Hai refund đồng thời | Tổng refund không vượt paid amount; provider gọi một lần | Reservation và Row Version chặn over-refund | Refund cap và Idempotency | `ConcurrentOnlineRefunds_ReserveAmountAndCallProviderOnce` | VERIFIED |
| Checkout persistence lỗi | Outbox insert lỗi trong Transaction | Checkout | Không có partial business commit | Order, inventory và cart rollback | Atomic checkout | `OutboxWriteFailure_RollsBackOrderInventoryAndCart` | VERIFIED |
| Bỏ lỡ Webhook | Stripe-like payment ở `Processing` | Reconciliation đọc trạng thái `Paid` | Payment chuyển `Paid` một lần | Một history được ghi; replay là no-op | Payment state có thể phục hồi | `Reconciliation_RecoversSucceededPaymentWhenWebhookWasMissed` | VERIFIED |
| SQL backup/restore | Latest schema và critical fixture đã commit | Backup, phá schema/data rồi restore | Fixture và schema trở lại đầy đủ | Transaction, inventory, Outbox, audit và FX snapshot còn nguyên | Recoverability | Recovery integration test | VERIFIED |
| SQL tạm ngừng khi API chạy | API ready khi SQL healthy | Restart SQL và API | Readiness 503 tạm thời rồi hồi 200 | Process không crash; database và volume còn nguyên | Readiness trung thực và dữ liệu bền vững | Docker restart drill | VERIFIED |

## Kết quả hiệu năng

Dataset local gồm 20.000 product, 2.000 image row, 5.000 order lịch sử và checkout 50 dòng. API và
SQL Server chạy trên một máy, vì vậy đây là regression baseline, không phải load test hoặc capacity
forecast.

| Luồng | p95 | Budget | Trạng thái |
| --- | ---: | ---: | --- |
| Catalog | 44,2 ms | 500 ms | VERIFIED |
| Keyword search | 265,6 ms | 750 ms | VERIFIED |
| Image-heavy summary | 72,1 ms | 750 ms | VERIFIED |
| Order-history summary | 30,7 ms | 750 ms | VERIFIED |
| Admin dashboard | 64,7 ms | 1.000 ms | VERIFIED |
| Revenue report | 28,1 ms | 1.500 ms | VERIFIED |
| Login | 320,9 ms | 1.000 ms | VERIFIED |
| Refresh | 16,0 ms | 1.000 ms | VERIFIED |
| Session validation | 16,0 ms | 500 ms | VERIFIED |
| Checkout COD 50 dòng | 366,0 ms | 2.000 ms | VERIFIED |

p50, p99, throughput, concurrency và giới hạn phép đo nằm trong `docs/PERFORMANCE.md`.

## Kết quả rà soát bảo mật

- Không có package direct/transitive bị NuGet Advisory báo vulnerable từ source baseline.
- Không có high-confidence secret trong source release candidate; scan lịch sử audit không phát
  hiện credential khớp mẫu high-confidence.
- Staging/Production từ chối JWT placeholder, auth URL HTTP, generic Webhook secret yếu, SQL/host
  config không an toàn, Data Protection path tương đối và SMTP không TLS.
- Upload từ chối SVG, extension/MIME mismatch, magic bytes sai, file vượt 5 MB và path traversal.
- Health detail yêu cầu Admin; public health chỉ trả trạng thái tổng.
- Không có confirmed Critical/High finding trong phạm vi static review và test đã chạy.

Trạng thái: `VERIFIED` với các gate trên. Penetration test, DAST, cloud IAM review và host hardening
là `NOT_REQUIRED` với source-only local gate, nhưng phải được đánh giá riêng trước public production.

## Giới hạn đã biết

- Một API instance; rate limiter và FX cache chạy trong process.
- Product image dùng local/shared durable volume, chưa phải object storage.
- Email verification chưa bắt buộc để login.
- SMTP/Outbox là at-least-once.
- Payment reconciliation chưa tự xử lý provider-pending refund.
- Dashboard/report dùng base currency VND; đổi base currency không phải thay đổi config đơn thuần.
- Local performance không mô phỏng network, ingress, noisy neighbor hoặc production traffic mix.

Chi tiết và trigger nâng cấp nằm tại `docs/LIMITATIONS.md`.

## Hành động cần môi trường bên ngoài

- [x] Commit/push source release candidate và xác nhận Backend CI của đúng SHA đạt.
- [ ] Provision staging host, trusted reverse proxy, DNS và TLS certificate.
- [ ] Thiết lập staging secret store cho SQL, JWT và Data Protection volume.
- [ ] Cung cấp Stripe test secret, publishable key và Webhook secret; chạy success, replay, invalid
  signature, missed Webhook, reconciliation và partial/full refund.
- [ ] Cung cấp CurrencyAPI key; chạy VND/USD/EUR, cache và outage scenario.
- [ ] Cung cấp SMTP staging credential; xác minh inbox cho email verification/password reset và
  retry/dead-letter.
- [ ] Kết nối OTLP collector/dashboard và kích hoạt ít nhất một readiness, Outbox hoặc payment alert.
- [ ] Thiết lập backup scheduler, off-host retention, RPO/RTO và restore drill trên staging.
- [ ] Chạy deployment và rollback rehearsal bằng release artifact trên staging.

Không ghi secret hoặc provider payload nhạy cảm vào issue/report khi hoàn thành checklist.

## Kết luận

**Trạng thái cuối: RELEASE CANDIDATE WITH EXTERNAL BLOCKERS.**

**Release Candidate: YES.** Source, data consistency, local recoverability, regression, performance
baseline, release artifact và Docker topology đã có bằng chứng đạt.

**Production Verified: NO.** Staging HTTPS, Stripe/CurrencyAPI/SMTP E2E, collector alert và backup
operation thật chưa được xác minh.

**Tag `v1.0.0`: NOT CREATED.** Chỉ tạo tag sau khi tất cả external action bắt buộc đạt trên cùng
commit SHA và báo cáo được cập nhật bằng ID/bằng chứng không chứa secret.

Feature scope tiếp tục được đóng băng; chỉ mở lại khi có bug, security issue, operational evidence,
real user feedback hoặc business requirement mới.
