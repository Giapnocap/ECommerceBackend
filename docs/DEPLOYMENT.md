# Deployment Guide

This guide covers the production checklist for the ECommerceBackend API.

## Required Runtime Settings

Use environment variables, user secrets, a secret manager, or your hosting provider's secret store. Do not commit production secrets to `appsettings.json`.

Required in production:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__Default=Server=...;Database=ECommerceDB;...;Encrypt=True;TrustServerCertificate=False;
AllowedHosts=api.example.com
DataProtection__ApplicationName=ECommerceBackend
DataProtection__KeysPath=<absolute persistent path outside the app package>
Jwt__Key=<at least 32 bytes, not the development key>
Jwt__Issuer=ECommerceBackend
Jwt__Audience=<expected API audience>
Jwt__AccessTokenMinutes=60
Jwt__RefreshTokenDays=7
Cors__AllowedOrigins__0=https://your-frontend.example.com
Uploads__MaxImageSizeBytes=5242880
Uploads__ReconciliationGraceMinutes=60
Uploads__MaxReconciliationDeletes=100
Swagger__Enabled=false
AdminBootstrap__Enabled=false
Outbox__Enabled=true
Outbox__ProcessingTimeoutSeconds=60
Outbox__MaxPendingAgeMinutes=15
OrderLifecycle__PendingCodHoldMinutes=30
OrderLifecycle__MaxPendingOrdersPerCustomer=3
OrderLifecycle__ExpirationEnabled=true
OrderLifecycle__ExpirationDryRun=true
OrderLifecycle__ExpirationPollIntervalSeconds=30
OrderLifecycle__ExpirationBatchSize=50
OrderLifecycle__MaxOverdueMinutes=15
Notifications__Smtp__Enabled=true
Notifications__Smtp__Host=<smtp-host>
Notifications__Smtp__Port=587
Notifications__Smtp__EnableSsl=true
Notifications__Smtp__TimeoutSeconds=30
Notifications__Smtp__UserName=<smtp-user>
Notifications__Smtp__Password=<smtp-password>
Notifications__Smtp__FromAddress=no-reply@example.com
Serilog__MinimumLevel=Information
```

When the API is behind a reverse proxy, also configure its exact address or CIDR:

```text
ReverseProxy__Enabled=true
ReverseProxy__ForwardLimit=1
ReverseProxy__RequireHeaderSymmetry=true
ReverseProxy__KnownProxies__0=10.0.0.10
```

Optional:

```text
AutoMapper__LicenseKey=<license key from your AutoMapper account>
PaymentWebhooks__GenericHmac__Enabled=true
PaymentWebhooks__GenericHmac__ProviderCode=<provider-code>
PaymentWebhooks__GenericHmac__Secret=<at least 32 bytes from a secret store>
PaymentWebhooks__GenericHmac__MaxFutureSkewMinutes=5
```

The app validates JWT, CORS, payment webhook, outbox and SMTP settings on startup. Production
also fails closed for insecure SQL TLS, wildcard/localhost `AllowedHosts`, a non-absolute Data
Protection path, or an enabled reverse proxy without a trusted proxy/network.

## Payment Webhook

The generic webhook endpoint is:

```text
POST /api/payments/webhooks/{providerCode}
X-Payment-Event-Id: <stable provider event ID>
X-Payment-Signature: <hex HMAC-SHA256>
Content-Type: application/json
```

Calculate the signature over the exact UTF-8 bytes of:

```text
eventId + "." + rawJsonBody
```

`paid` and `refunded` payloads must include the exact expected payment amount:

```json
{
  "providerTransactionId": "provider-transaction-id",
  "status": "paid",
  "amount": 100.00,
  "occurredAt": "2026-07-19T12:00:00Z"
}
```

Reject provider timestamps beyond `MaxFutureSkewMinutes` instead of accepting future-dated
payment history. Keep server clocks synchronized with a trusted NTP source.

Keep the webhook secret in a secret store and restrict the endpoint at the gateway when the
provider publishes stable source IP ranges. COD does not use this webhook.

## Notification Outbox

The outbox dispatcher is enabled by default. Development and test environments may leave SMTP
disabled for log-only delivery. Production startup fails when the outbox is enabled without SMTP.

Workers claim one message at a time, bound delivery by `Outbox__ProcessingTimeoutSeconds`, retry
with exponential backoff, and move exhausted messages to dead-letter state after
`Outbox__MaxAttempts`. The lock timeout must be longer than the processing timeout. Delivery is
at-least-once: the SMTP adapter includes the outbox message ID in `X-Idempotency-Key`, and any
additional downstream adapter must deduplicate by that value.

## First Administrator

Roles and permissions are seeded, but administrator passwords are not. For the first startup
only, provide these values through a secret store:

```text
AdminBootstrap__Enabled=true
AdminBootstrap__UserName=<admin username>
AdminBootstrap__Email=<admin email>
AdminBootstrap__Password=<strong 12-128 character secret>
AdminBootstrap__FullName=<display name>
```

After the startup log confirms creation or recovery of the administrator, set
`AdminBootstrap__Enabled=false` and restart. The bootstrapper will not overwrite an existing,
usable administrator.

## Persistent Storage

Persist these folders outside the app package:

```text
DataProtectionKeys/
Uploads/
logs/
```

`DataProtection__KeysPath` must point to the persistent Data Protection mount in production.
Losing or replacing these keys can invalidate protected payloads. All instances must share the
same path and `DataProtection__ApplicationName`. `Uploads` contains product images. `logs`
contains rolling Serilog files.

## Database Migration

Recommended migration flow:

1. Back up the production database.
2. Review migrations locally:

```bash
dotnet ef migrations list
```

3. Apply migrations during a controlled deployment window:

```bash
dotnet ef database update --connection "<production-connection-string>"
```

4. Verify readiness:

```bash
curl https://your-api.example.com/health/ready
```

Do not run destructive database commands directly against production without a backup and rollback plan.

Before production, run the SQL Server integration flow against a dedicated test instance:

```powershell
$env:RUN_SQL_INTEGRATION_TESTS="1"
$env:ECOMMERCE_TEST_SQL_CONNECTION="<test SQL Server connection string>"
dotnet test --filter "Category=SqlServerIntegration"
```

## Publish And Run

Publish:

```bash
dotnet publish -c Release -o ./publish
```

The project excludes `appsettings.Local.json` and configuration templates from build/publish
artifacts. Supply production secrets through environment variables or a secret store; do not
copy the local settings file into the published directory.


Run:

```bash
dotnet ECommerceBackend.dll
```

When hosted behind a reverse proxy, terminate TLS at the proxy or at Kestrel and forward traffic
to the application. Only configure proxy addresses owned by the deployment; never use a catch-all
network. Forwarded headers run before HTTPS redirect, request logging and IP-based rate limiting,
preventing redirect loops while preserving the real client address. In production the app enables
HSTS and HTTPS redirection.

## Health Checks

Endpoints:

```text
GET /health/live   - application process is running
GET /health/ready  - database connectivity, outbox backlog and order expiration
GET /health        - all registered checks
GET /health/details - detailed checks, Admin authentication required
```

Use `/health/live` for liveness probes and `/health/ready` for readiness probes. Readiness becomes
unhealthy when the oldest pending outbox message exceeds `Outbox__MaxPendingAgeMinutes`; dead-letter
messages report a degraded state. Public endpoints return only aggregate status; check names,
descriptions, timings and diagnostic data are available only from `/health/details` to an Admin.

Deploy the additive order-lifecycle migration before enabling expiration. Start with
`OrderLifecycle__ExpirationDryRun=true`, inspect overdue counts and readiness, then switch it to
`false`. Set `OrderLifecycle__ExpirationEnabled=false` for an immediate worker rollback; checkout,
customer cancellation and existing order APIs remain available.

## Request Correlation And Logs

The API returns `X-Correlation-ID` on requests that reach the application pipeline. Clients may
send the same header using 1-128 ASCII letters, digits, dots, underscores or hyphens. Do not put
user data, credentials or other secrets in correlation IDs.

Error-response `traceId`, Serilog request events and rolling files under `logs/` share this ID.
Set `Serilog__MinimumLevel` through deployment configuration to control runtime verbosity. A
client-disconnected request is logged as an abort rather than a server failure; internal status
`499` is used for request logging and is normally not observable by the disconnected client.

API failures use `Content-Type: application/problem+json`. Monitoring and clients should use the
numeric `status` and stable `code`; `traceId` links the response to logs. Existing clients may
continue reading `message`, `details` and field-level `errors`.

Operational recovery for outbox dead letters, upload reconciliation, audit investigation and
metrics is documented in `docs/OPERATIONS_RUNBOOK.md`.

## Swagger

Swagger is enabled automatically in development. In production it is disabled unless:

```text
Swagger__Enabled=true
```

Keep Swagger disabled publicly unless access is restricted by network, gateway, or authentication rules.

## Smoke Test

After deployment:

1. Check `/health/live`.
2. Check `/health/ready`.
3. Login with the bootstrapped admin account.
4. Create a category and product.
5. Register/login a customer.
6. Add product to cart and place an order with a unique `Idempotency-Key` header.
7. Retry the same checkout key and verify the same order is returned.
8. Update order status as Admin or Staff.
9. Check inventory history and the sales summary endpoints.
