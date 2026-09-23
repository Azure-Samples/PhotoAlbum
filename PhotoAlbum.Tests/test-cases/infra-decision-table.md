# Infra Decision Table

> This document is part of the **frozen baseline bundle**. It records, for every external dependency the migrated application talks to, whether the post-migration tests will exercise it as a **real** provisioned resource, a **testcontainer**-backed emulator/dependency, or a **mock** at the SDK / HTTP boundary. The decision is made once here and reused as-is by the `verify-test-baseline` skill.

## Metadata

| Field | Value |
|-------|-------|
| Project | PhotoAlbum |
| Module | `PhotoAlbum` |
| Migration Scope | Remove remaining local file-based I/O / synchronous file APIs so photo storage relies solely on Azure Blob Storage; externalize local application configuration to Azure App Configuration; evaluate/modernize static web asset hosting; scan and remediate known CVEs; deploy to existing Azure Container Apps infrastructure. |
| Integration Test Environment | mock |
| Created At | 2026-09-23 |
| `infra/` Snapshot | infra-missing — `infra/` exists (`infra/main.bicep`, `infra/main.json`, `infra/main.parameters.json`) but the confirmed Integration Test Environment for this migration is explicitly `mock` (per `.github/modernize/modernization-plan/plan.md` Open Questions: "Should the plan include integration testing to verify migrated services? → Yes — Mock mode (no live Azure infra required for tests)" and task `005-integrationTest` requirements: "Use Mock mode: mock Azure Blob Storage, Azure SQL Database, and Azure App Configuration dependencies."). Per the decision rules, mode = `mock` forces every dependency's Decision to `mock` regardless of `infra/` presence. |
| Status | baseline (frozen) |

## Decision Table

| Dependency | Infra Match | Decision | Auth Method | Reason |
|---|---|---|---|---|
| Azure Blob Storage (photo storage container, configured via `Storage:ServiceUri` / `Storage:AccountName` + `Storage:ContainerName`, accessed through `IPhotoBlobStorage` / `AzurePhotoBlobStorage`) | Yes — `infra/main.bicep` provisions a storage account, but not applicable given confirmed mock mode | mock | n/a | Integration Test Environment is confirmed `mock` for this migration (see Metadata); mocked at the `IPhotoBlobStorage` interface boundary regardless of `infra/` presence. |
| Azure SQL Database (`Photos` table, accessed through EF Core `PhotoAlbumContext` / `ConnectionStrings:DefaultConnection` or `AzureSql:Server`+`AzureSql:Database`) | Yes — `infra/main.bicep` provisions a SQL server/database, but not applicable given confirmed mock mode | mock | n/a | Integration Test Environment is confirmed `mock` for this migration; mocked via an in-process substitute for `PhotoAlbumContext` (e.g. EF Core InMemory provider, consistent with the existing unit test's approach) regardless of `infra/` presence. |
| Azure App Configuration (`AZURE_APP_CONFIGURATION_ENDPOINT`, loaded in `Program.cs` via `AddAzureAppConfiguration`) | Yes — `infra/main.bicep` may provision App Configuration, but not applicable given confirmed mock mode | mock | n/a | Integration Test Environment is confirmed `mock` for this migration; the test host supplies configuration directly (e.g. via `IConfiguration` in-memory collection / test `appsettings`) instead of connecting to a live App Configuration store. |
| Azure Key Vault (optional, `AZURE_KEY_VAULT_URI`, loaded in `Program.cs` via `AddAzureKeyVault` only when the env var is set) | No | mock | n/a | Not one of the three dependencies in migration scope; conditionally wired only when `AZURE_KEY_VAULT_URI` is set. Test host leaves this env var unset so the code path is not exercised, which is itself the mock strategy (no live Key Vault call is made). |

### Notes

- All four rows resolve to `mock` primarily because the plan's confirmed Integration Test Environment is `mock`; the `infra/` folder's presence is recorded for audit purposes but does not change the decision per the "If the confirmed mode is `mock`: mark every dependency mock regardless of `infra/` presence" rule.
- `Auth Method = n/a` is used for all rows, which is valid because `Decision = mock` for every row.
- No cross-module or third-party dependencies exist in this single-module application beyond the three Azure services listed in the migration scope and the optional Key Vault path.
