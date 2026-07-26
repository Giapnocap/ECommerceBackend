# Backend Baseline

This document records the contract and quality baseline that must remain stable while the backend is improved.

## Scope

- Architecture: ASP.NET Core 8 layered modular monolith.
- Persistence: Entity Framework Core with SQL Server.
- Deployment unit: one API process and one SQL Server database.
- Latest migration: `20260726065143_OptimizeCatalogDefaultSort`.
- Payment checkout method: cash on delivery.
- Notification delivery: transactional outbox with at-least-once delivery.
- Product images: local file storage for the current single-instance scope.

## Authorization Matrix

| Area | Anonymous | Customer | Staff | Admin |
|---|---:|---:|---:|---:|
| Catalog reads | Yes | Yes | Yes | Yes |
| Profile | No | Own account | Own account | Own account |
| Cart and checkout | No | Own data | No | No |
| Customer order history and cancellation | No | Own orders | No | No |
| Order processing | No | No | Yes | Yes |
| Inventory reads | No | No | Yes | Yes |
| Catalog administration | No | No | No | Yes |
| User administration | No | No | No | Yes |
| Reports | No | No | No | Yes |
| Audit, outbox recovery and retention | No | No | No | Yes |

Role claims are informational for most administration endpoints. Permission policies protect business capabilities; operations endpoints additionally require the `Admin` role.

## Versioned Contracts

- `docs/contracts/openapi-v1.json` is the canonical HTTP contract.
- `OpenApiBaselineTests` fails when the generated OpenAPI document changes.
- Update the snapshot only after reviewing backward compatibility:

```powershell
$env:UPDATE_OPENAPI_BASELINE = "1"
dotnet test ECommerceBackend.Tests/ECommerceBackend.Tests.csproj `
  --filter "FullyQualifiedName~OpenApiBaselineTests"
Remove-Item Env:UPDATE_OPENAPI_BASELINE
```

## Quality Gates

The baseline is accepted only when all of the following pass:

- Release build with zero warnings.
- Formatting verification.
- EF Core pending-model check.
- Unit, domain, application and API contract tests.
- SQL Server transaction and concurrency tests.
- Migration forward/rollback/forward test.
- SQL Server backup/restore recovery test.
- Opt-in SQL Server performance budgets for catalog, session validation and checkout.
- Line coverage of at least 75 percent and branch coverage of at least 60 percent.
- NuGet vulnerability audit and tracked-file secret scan.

Migration and release SQL files are generated artifacts. They are verified by checksum and CI rather than committed to source control.
