# ECommerceBackend Production Readiness Report

## Metadata

| Mục | Giá trị |
|---|---|
| Audit baseline commit | `2f6afd92b59c9dbeb9b16b486bafc39ec9b9fbb1` |
| Audit source | Release-candidate source commit `6c04359632bf4fc5518ef3a715c7c34b1acaafd3` |
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
| Automated tests | Unit, integration, SQL, recovery và performance đạt | VERIFIED | 652 test pass trong các batch độc lập | External provider E2E không thuộc deterministic suite | Chạy checklist external |
| Coverage | Line 82,86%, branch 66,24% | VERIFIED | `VerifyCoverage.ps1` với gate 80%/60% | Không phải độ bảo đảm tuyệt đối | Theo dõi regression |
| Migration | Artifact, checksum, upgrade/rollback/upgrade và model drift đạt | VERIFIED | 25 SQL tests; `has-pending-model-changes` sạch | Chưa chạy trên staging data volume | Staging backup + migration rehearsal |
| Backup/restore | Latest schema và committed marker phục hồi đúng | VERIFIED | 1/1 `SqlServerRecoveryIntegration` local | Chưa phải backup production | Thiết lập scheduler và restore drill staging |
| Docker | Build, migration ordering, non-root API, health và volume restart đạt | VERIFIED | migration exit 0; live/ready 200; 3 persistence marker pass | Chưa chạy trên deployment host | Re-run với staging registry/host |
| Security | Config fail-closed, RBAC tests, dependency/secret scan đạt | VERIFIED | 65 security tests; NuGet advisory sạch; scanner sạch | Không có pentest/DAST bên ngoài | Thực hiện theo risk profile |
| Observability | Structured log, correlation, OTel metrics/traces và health có sẵn | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | 27 tests; `docs/MONITORING.md` | Chưa nối collector/dashboard/alert thật | Cấu hình OTLP và fire alert staging |
| Stripe | Gateway, raw webhook validation, idempotency, reconciliation, refund được test bằng adapter deterministic | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | Payment/refund tests và SQL concurrency pass | Không có Stripe test credentials/webhook delivery | Chạy Stripe Test Mode E2E |
| FX provider | Cache, single-flight, timeout, stale bound và USD/EUR snapshot được test | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | CurrencyAPI adapter tests pass | Không có API key/quota thật | Chạy CurrencyAPI staging |
| SMTP | Token lifecycle, outbox và SMTP config/TLS validation có sẵn | IMPLEMENTED_NOT_EXTERNAL_VERIFIED | Auth/outbox/config tests pass | Không có SMTP credential và inbox thật | Gửi verify/reset mail staging |
| Staging HTTPS | Template, host/CORS/proxy/TLS validation đã chuẩn bị | BLOCKED_EXTERNAL | `appsettings.Staging.example.json` và startup tests | Chưa có host, DNS, TLS, trusted proxy | Provision staging và chạy smoke |
| CI của release candidate | Ba job của Backend CI đã đạt | VERIFIED | [GitHub Actions run 32372049844](https://github.com/Giapnocap/ECommerceBackend/actions/runs/32372049844) cho commit `6c04359` | Không | Duy trì CI gate sau mỗi push |
| `v1.0.0` | Chưa tạo tag | BLOCKED_EXTERNAL | Không có production/external verification đầy đủ | Các mục trên còn block | Chỉ tag sau khi mọi gate bắt buộc xanh |

## Build và test

| Gate | Result | Status |
|---|---:|---|
| `dotnet format --verify-no-changes` | Sạch | VERIFIED |
| Release solution build | 0 warning, 0 error | VERIFIED |
| Unit tests | 279/279 | VERIFIED |
| Integration/contract tests không dùng SQL tagged | 346/346 | VERIFIED |
| SQL Server integration | 25/25 | VERIFIED |
| SQL Server backup/restore | 1/1 | VERIFIED |
| SQL Server performance | 1/1 | VERIFIED |
| Tổng test của full gate | 652 pass, 0 fail, 0 skip | VERIFIED |
| Line/branch coverage | 82,86% / 66,24% | VERIFIED |
| Migration model drift | Không có | VERIFIED |
| Release package checksum/manifest/smoke | Đạt | VERIFIED |

Các batch targeted chạy trong audit được dùng để định vị lỗi nhưng không cộng lặp vào tổng 652.

## Database và recoverability

- Latest migration: `20260818210000_AddRefundMoneySnapshots`.
- Rollback target trong artifact: `20260818200000_AddMoneySnapshots`.
- Idempotent forward script chạy được; rollback một migration rồi forward lại thành công.
- Rollback snapshot người nhận từ chối dữ liệu không thể bảo toàn thay vì âm thầm xóa.
- Backup/restore khôi phục latest schema và committed marker trên SQL Server thật.
- Docker SQL volume giữ dữ liệu qua container restart.

Status: `VERIFIED` cho local/isolated SQL Server. Backup scheduler, off-host copy, RPO/RTO và restore
drill staging/production là `BLOCKED_EXTERNAL`.

## Failure và consistency matrix

| Scenario | Kết quả/invariant | Evidence | Status |
|---|---|---|---|
| Hai Customer tranh sản phẩm cuối | Chỉ một order commit | `ConcurrentCustomers_CompetingForLastItem_CreateOneOrder` | VERIFIED |
| Retry checkout cùng key | Một logical order | `ConcurrentDuplicateCheckout_ReturnsOneCommittedOrder` | VERIFIED |
| Checkout và stock adjustment đồng thời | Stock và ledger nhất quán | Hai SQL concurrency tests | VERIFIED |
| Outbox write fail trong checkout | Order, inventory và cart rollback | `OutboxWriteFailure_RollsBackOrderInventoryAndCart` | VERIFIED |
| Payment initialization retry | Một provider creation; network ngoài transaction | `ExternalCreation_IsIdempotentAndRunsOutsideDatabaseTransaction` | VERIFIED |
| Duplicate/mutated webhook | Replay idempotent; payload đổi bị từ chối | Payment webhook suite | VERIFIED |
| Webhook amount/currency sai | Không mutation | Hai mismatch tests | VERIFIED |
| Webhook bị lỡ | Reconciliation phục hồi paid state | `Reconciliation_RecoversSucceededPaymentWhenWebhookWasMissed` | VERIFIED |
| Provider reconciliation mismatch | Không mutation và còn retry được | Provider mismatch test | VERIFIED |
| Hai refund đồng thời | Reserve amount, provider gọi một lần, không over-refund | SQL refund concurrency test | VERIFIED |
| Outbox worker cạnh tranh/restart | Lease và delivery invariant được giữ | Outbox SQL/integration tests | VERIFIED |
| Nhận hàng hoàn đồng thời | Tồn kho hoàn đúng một lần | SQL return receipt test | VERIFIED |
| Password reset đồng thời | Một commit, session bị thu hồi | SQL password reset test | VERIFIED |
| FX provider lỗi | Stale cache có giới hạn; quá hạn fail ổn định | CurrencyAPI failure tests | VERIFIED |
| SQL backup/restore | Latest schema và committed marker còn nguyên | Recovery integration test | VERIFIED |

## Performance results

Dataset local: 20.000 product, 2.000 image row, 5.000 order lịch sử và checkout 50 dòng. API cùng
SQL Server chạy trên một máy nên đây là regression baseline, không phải load/capacity test.

| Path | p95 | Budget | Status |
|---|---:|---:|---|
| Catalog | 41,2 ms | 500 ms | VERIFIED |
| Keyword search | 240,9 ms | 750 ms | VERIFIED |
| Image-heavy summary | 58,3 ms | 750 ms | VERIFIED |
| Order-history summary | 26,9 ms | 750 ms | VERIFIED |
| Admin dashboard | 36,5 ms | 1.000 ms | VERIFIED |
| Revenue report | 25,2 ms | 1.500 ms | VERIFIED |
| Login | 134,3 ms | 1.000 ms | VERIFIED |
| Refresh | 13,8 ms | 1.000 ms | VERIFIED |
| Session validation | 20,1 ms | 500 ms | VERIFIED |
| 50-line COD checkout | 229,6 ms | 2.000 ms | VERIFIED |

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

**Release Candidate: YES.** Source, data consistency, local recoverability, regression, performance
baseline, release artifact và Docker topology đã có bằng chứng đạt.

**Production Verified: NO.** Staging HTTPS, Stripe/CurrencyAPI/SMTP E2E, collector alerts và backup
operation thật chưa được xác minh.

**Tag `v1.0.0`: NOT CREATED.** Chỉ tạo tag sau khi tất cả external actions bắt buộc đạt trên cùng
commit SHA và báo cáo được cập nhật bằng ID/bằng chứng không chứa secret.
