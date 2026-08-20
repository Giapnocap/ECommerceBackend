# ECommerceBackend Production Readiness Report

## Metadata

| Mục | Giá trị |
|---|---|
| Audit baseline commit | `b8a14616d37dd4e9a7fed541ce0a2a5fb148fad0` trên branch `main` |
| Audit source | Baseline trên cộng với recovery test và tài liệu trong working tree hiện tại |
| Audit date | 2026-08-20 |
| Local environment | Windows `10.0.22621`, .NET `8.0.25`, 12 logical processors |
| Database verification | SQL Server 2022 Linux container, Docker Engine `29.7.2` |
| Plan source | External final-readiness plan; không lưu plan nội bộ vào repository |

Status được dùng trong báo cáo: `VERIFIED`, `IMPLEMENTED_NOT_EXTERNAL_VERIFIED`,
`BLOCKED_EXTERNAL`, `FAILED`, `NOT_REQUIRED`.

## Trạng thái tổng quan

| Area | Current State | Status | Evidence | Gap | Action |
|---|---|---|---|---|---|
| Documentation | Architecture, ERD, sequence, limitations, monitoring, demo và runbook đồng bộ | VERIFIED | Markdown review, stale-text scan, API/source đối chiếu | Chưa review bởi operator ngoài dự án | Review trong staging handover |
| Release build | Release build không warning/error | VERIFIED | `dotnet build ... --configuration Release --no-restore` | Không | Giữ CI gate |
| Automated tests | Unit, integration, SQL, recovery và performance đạt | VERIFIED | 653 test pass trong các batch độc lập | External provider E2E không thuộc deterministic suite | Chạy checklist external |
| Coverage | Line 82,88%, branch 66,30% | VERIFIED | `VerifyCoverage.ps1` với gate 80%/60% | Không phải độ bảo đảm tuyệt đối | Theo dõi regression |
| Migration | Artifact, checksum, upgrade/rollback/upgrade và model drift đạt | VERIFIED | 25 SQL tests; `has-pending-model-changes` sạch | Chưa chạy trên staging data volume | Staging backup + migration rehearsal |
| Backup/restore | Latest schema và dữ liệu nghiệp vụ quan trọng phục hồi đúng | VERIFIED | 1/1 `SqlServerRecoveryIntegration` local | Chưa phải backup production | Thiết lập scheduler và restore drill staging |
| Docker | Build, migration ordering, non-root API, health và volume restart đạt | VERIFIED | migration exit 0; live/ready 200; DB/uploads/keys/logs còn nguyên sau restart | Chưa chạy trên deployment host | Re-run với staging registry/host |
| Security | Config fail-closed, RBAC tests, dependency/secret scan đạt | VERIFIED | Targeted security/config tests; NuGet advisory sạch; scanner sạch | Không có pentest/DAST bên ngoài | Thực hiện theo risk profile |
| Observability | Structured log, correlation, OTel metrics/traces và health có sẵn | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | 27 tests; `docs/MONITORING.md` | Chưa nối collector/dashboard/alert thật | Cấu hình OTLP và fire alert staging |
| Stripe | Gateway, PaymentIntent và raw webhook validation được test bằng adapter deterministic | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | Gateway/webhook/idempotency tests pass | Không có Stripe test credentials/webhook delivery | Chạy Stripe Test Mode E2E |
| Refund | Partial/full refund, cumulative cap, original currency và concurrency được bảo vệ | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | Refund tests và SQL concurrency pass | Chưa gọi Stripe Test Mode thật | Chạy partial/full refund E2E |
| Reconciliation | Worker phục hồi missed webhook và từ chối amount/currency mismatch | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | Reconciliation success/mismatch tests pass | Chưa đối chiếu Stripe Test Mode thật | Chạy missed-webhook E2E |
| FX provider | Cache, single-flight, timeout, stale bound và USD/EUR snapshot được test | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | CurrencyAPI adapter tests pass | Không có API key/quota thật | Chạy CurrencyAPI staging |
| SMTP | Token lifecycle, outbox và SMTP config/TLS validation có sẵn | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | Auth/outbox/config tests pass | Không có SMTP credential và inbox thật | Gửi verify/reset mail staging |
| Staging HTTPS | Template, host/CORS/proxy/TLS validation đã chuẩn bị | BLOCKED_EXTERNAL | `appsettings.Staging.example.json` và startup tests | Chưa có host, DNS, TLS, trusted proxy | Provision staging và chạy smoke |
| CI của release candidate | Ba job của Backend CI đã đạt | VERIFIED | [GitHub Actions run 32372618005](https://github.com/Giapnocap/ECommerceBackend/actions/runs/32372618005) cho baseline `b8a1461` | Working-tree audit delta chưa push | Chạy lại CI sau commit tiếp theo |
| `v1.0.0` | Chưa tạo tag | BLOCKED_EXTERNAL | Không có production/external verification đầy đủ | Các mục trên còn block | Chỉ tag sau khi mọi gate bắt buộc xanh |

## Build và test

| Gate | Result | Status |
|---|---:|---|
| `dotnet format --verify-no-changes` | Sạch | VERIFIED |
| Release solution build | 0 warning, 0 error | VERIFIED |
| Unit tests | 279/279 | VERIFIED |
| Integration/contract tests không dùng SQL tagged | 347/347 | VERIFIED |
| SQL Server integration | 25/25 | VERIFIED |
| SQL Server backup/restore | 1/1 | VERIFIED |
| SQL Server performance | 1/1 | VERIFIED |
| Tổng test của full gate | 653 pass, 0 fail, 0 skip | VERIFIED |
| Line/branch coverage | 82,88% / 66,30% | VERIFIED |
| Migration model drift | Không có | VERIFIED |
| Release package checksum/manifest/smoke | Đạt | VERIFIED |

Các batch targeted chạy trong audit được dùng để định vị lỗi nhưng không cộng lặp vào tổng 653.

## Database và recoverability

- Latest migration: `20260818210000_AddRefundMoneySnapshots`.
- Rollback target trong artifact: `20260818200000_AddMoneySnapshots`.
- Idempotent forward script chạy được; rollback một migration rồi forward lại thành công.
- Rollback snapshot người nhận từ chối dữ liệu không thể bảo toàn thay vì âm thầm xóa.
- Backup/restore khôi phục latest schema cùng User, Order, OrderDetail, Payment, Product,
  InventoryTransaction, OutboxMessage, AuditEvent và snapshot VND/USD trên SQL Server thật.
- Docker SQL volume giữ dữ liệu qua container restart.

Status: `VERIFIED` cho local/isolated SQL Server. Backup scheduler, off-host copy, RPO/RTO và restore
drill staging/production là `BLOCKED_EXTERNAL`.

## Failure và consistency matrix

| Scenario | Initial State | Action | Expected Result | Actual Result | Invariant Protected | Evidence | Status |
|---|---|---|---|---|---|---|---|
| Duplicate checkout | Một cart, một idempotency key | Gửi hai request đồng thời | Một logical order | Một order được trả cho cả hai request | Không duplicate order/stock mutation | `ConcurrentDuplicateCheckout_ReturnsOneCommittedOrder` | VERIFIED |
| Concurrent checkout | Hai Customer tranh một sản phẩm cuối | Checkout đồng thời | Chỉ một order commit, stock không âm | Một success, một request bị từ chối an toàn | Non-negative stock, no lost update | `ConcurrentCustomers_CompetingForLastItem_CreateOneOrder` | VERIFIED |
| Stock adjustment vs checkout | Product có stock hữu hạn | Adjustment và checkout đồng thời | Ledger khớp stock cuối | Stock và ledger nhất quán | Serialized inventory mutation | Hai SQL inventory concurrency tests | VERIFIED |
| Duplicate payment creation | Card payment chưa có provider ID | Gọi initialize lặp lại | Cùng provider identity/idempotency key | Local payment chỉ gắn một provider ID; HTTP ngoài transaction | No double payment | `ExternalCreation_IsIdempotentAndRunsOutsideDatabaseTransaction` | VERIFIED |
| API interruption during payment | Provider trả PaymentIntent, local completion chưa commit | Hủy request rồi retry sau lease | Retry dùng cùng provider idempotency key và hoàn tất một lần | Cùng key, một local provider ID, lease được xóa | No orphan duplicate payment | `ExternalCreation_RequestInterruptedAfterProviderSuccess_RetriesWithSameIdempotencyKey` | VERIFIED |
| Duplicate/mutated webhook | Event đã được xử lý | Replay cùng payload hoặc đổi payload | Replay không side effect; mutation bị từ chối | Đúng như expected | Exactly-once local effect | Payment webhook replay/hash tests | VERIFIED |
| Webhook amount/currency mismatch | Payment đang active | Gửi signed event sai amount/currency | Không mutation | Payment/history/outbox không đổi | Payment/order money consistency | Hai mismatch tests | VERIFIED |
| Outbox worker crash/restart | Message đã gửi nhưng completion chưa commit | Mô phỏng crash, hết lease rồi chạy worker mới | Message được reclaim với cùng identity | Redelivery cùng Message-ID và mark processed | Durable at-least-once delivery | `CrashAfterDelivery_ReclaimsLeaseAndRedeliversSameOutboxMessage` | VERIFIED |
| SQL concurrency conflict | Paid webhook và cancellation cùng tranh order/payment | Chạy hai transaction đồng thời | State machine không tạo tổ hợp sai | Order/payment invariant được giữ | Stable lock order/state consistency | `ConcurrentPaidWebhookAndCancellation_PreserveOrderPaymentInvariant` | VERIFIED |
| FX provider failure | Cache fresh/stale/expired | Provider timeout/failure | Dùng stale trong giới hạn, ngoài giới hạn fail | Đúng như expected | Stable historical snapshot/no silent rate | CurrencyAPI failure tests | VERIFIED |
| Concurrent refund | Paid payment có refundable balance | Hai refund đồng thời | Tổng refund không vượt paid amount; provider gọi một lần | Reservation và row version chặn over-refund | Refund cap/idempotency | `ConcurrentOnlineRefunds_ReserveAmountAndCallProviderOnce` | VERIFIED |
| Checkout persistence failure | Outbox insert bị lỗi trong transaction | Checkout | Không có partial business commit | Order, inventory và cart rollback | Atomic checkout | `OutboxWriteFailure_RollsBackOrderInventoryAndCart` | VERIFIED |
| Missed webhook | Stripe-like payment ở Processing | Reconciliation đọc trạng thái Paid | Payment chuyển Paid một lần | Một history được ghi; replay no-op | Recoverable payment state | `Reconciliation_RecoversSucceededPaymentWhenWebhookWasMissed` | VERIFIED |
| SQL backup/restore | Latest schema và critical fixture đã commit | Backup, phá schema/data, restore | Toàn bộ fixture và schema trở lại | Transaction, inventory, outbox, audit, FX snapshot còn nguyên | Recoverability | Recovery integration test | VERIFIED |
| SQL tạm ngừng khi API chạy | API ready với SQL healthy | Restart SQL và API | Readiness 503 tạm thời rồi hồi 200 | Process không crash; DB/volumes còn nguyên | Honest readiness and persistence | Docker restart drill | VERIFIED |

## Performance results

Dataset local: 20.000 product, 2.000 image row, 5.000 order lịch sử và checkout 50 dòng. API cùng
SQL Server chạy trên một máy nên đây là regression baseline, không phải load/capacity test.

| Path | p95 | Budget | Status |
|---|---:|---:|---|
| Catalog | 44,2 ms | 500 ms | VERIFIED |
| Keyword search | 265,6 ms | 750 ms | VERIFIED |
| Image-heavy summary | 72,1 ms | 750 ms | VERIFIED |
| Order-history summary | 30,7 ms | 750 ms | VERIFIED |
| Admin dashboard | 64,7 ms | 1.000 ms | VERIFIED |
| Revenue report | 28,1 ms | 1.500 ms | VERIFIED |
| Login | 320,9 ms | 1.000 ms | VERIFIED |
| Refresh | 16,0 ms | 1.000 ms | VERIFIED |
| Session validation | 16,0 ms | 500 ms | VERIFIED |
| 50-line COD checkout | 366,0 ms | 2.000 ms | VERIFIED |

p50, p99, throughput, concurrency và giới hạn phép đo nằm trong `docs/PERFORMANCE.md`.

## Security findings

- Không có package direct/transitive bị NuGet Advisory báo vulnerable từ source hiện tại.
- Không có high-confidence secret trong source release candidate; scan lịch sử audit không phát hiện
  credential phù hợp mẫu high-confidence.
- Staging/Production từ chối JWT placeholder, auth URL HTTP, generic webhook secret yếu, SQL/host
  config không an toàn, Data Protection path tương đối và SMTP không TLS.
- Upload từ chối SVG, extension/MIME mismatch, magic bytes sai, vượt 5 MB và path traversal.
- Health detail yêu cầu Admin; public health chỉ trả status tổng.
- Không có confirmed Critical/High finding trong phạm vi static review và test đã chạy.

Status: `VERIFIED` cho các gate nêu trên. Penetration test, DAST, cloud IAM review và host hardening là
`NOT_REQUIRED` đối với source-only local gate, nhưng cần đánh giá riêng trước public production.

## Known limitations

- Một API instance; rate limiter và FX cache là process-local.
- Product image dùng local/shared durable volume, chưa phải object storage.
- Email verification chưa bắt buộc để login.
- SMTP/outbox là at-least-once.
- Payment reconciliation chưa tự sửa provider-pending refund.
- Dashboard/report dùng base currency VND; đổi base currency không phải config-only change.
- Local performance không mô phỏng network, ingress, noisy neighbor hoặc production traffic mix.

Chi tiết và trigger nâng cấp nằm tại `docs/LIMITATIONS.md`.

## External actions required

- [x] Commit/push source release candidate và xác nhận Backend CI của đúng SHA xanh.
- [ ] Provision staging host, trusted reverse proxy, DNS và TLS certificate.
- [ ] Thiết lập staging secret store cho SQL, JWT và Data Protection volume.
- [ ] Cung cấp Stripe test secret/publishable key/webhook secret; chạy success, replay, invalid
  signature, missed webhook, reconciliation và partial/full refund.
- [ ] Cung cấp CurrencyAPI key; chạy VND/USD/EUR, cache và outage scenario.
- [ ] Cung cấp SMTP staging credential; xác minh inbox cho email verification/password reset và
  retry/dead-letter.
- [ ] Kết nối OTLP collector/dashboard và fire ít nhất một readiness/outbox/payment alert.
- [ ] Thiết lập backup scheduler, off-host retention, RPO/RTO và restore drill staging.
- [ ] Chạy deployment + rollback rehearsal bằng release artifact trên staging.

Không ghi secret hoặc provider payload nhạy cảm vào issue/report khi hoàn thành checklist.

## Final recommendation

**Final state: RELEASE CANDIDATE WITH EXTERNAL BLOCKERS.**

**Release Candidate: YES.** Source, data consistency, local recoverability, regression, performance
baseline, release artifact và Docker topology đã có bằng chứng đạt.

**Production Verified: NO.** Staging HTTPS, Stripe/CurrencyAPI/SMTP E2E, collector alerts và backup
operation thật chưa được xác minh.

**Tag `v1.0.0`: NOT CREATED.** Chỉ tạo tag sau khi tất cả external actions bắt buộc đạt trên cùng
commit SHA và báo cáo được cập nhật bằng ID/bằng chứng không chứa secret.

Feature scope tiếp tục được đóng băng; chỉ mở lại khi có bug, security issue, operational evidence,
real user feedback hoặc business requirement mới.
