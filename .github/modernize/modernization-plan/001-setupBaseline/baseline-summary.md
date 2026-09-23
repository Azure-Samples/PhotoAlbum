# Baseline Summary — 001-setupBaseline

**Project**: PhotoAlbum
**Task**: `001-setupBaseline` — Capture a frozen baseline snapshot of the PhotoAlbum application (source, test-cases, testdata, and infra-decision-table) before any modernization changes are made.
**Date**: 2026-09-23
**Result**: ✅ Success — `passBuild=true`, `passUnitTests=true`

## Module Scoping Decision

PhotoAlbum is a single-module solution:

| Module | Test Source Root | In/Out of Scope | Reason |
|---|---|---|---|
| `PhotoAlbum` (ASP.NET Core Razor Pages web app) | `PhotoAlbum.Tests` | **In scope** | Directly accesses all three migration-relevant resources: Azure Blob Storage (`IPhotoBlobStorage`/`AzurePhotoBlobStorage`/`LocalPhotoBlobStorage`), Azure SQL Database (EF Core `PhotoAlbumContext`), and Azure App Configuration (`Program.cs` `AddAzureAppConfiguration`). |

No other module/project exists besides the test project itself (`PhotoAlbum.Tests`), which is the test source root and not a migration target.

## Entry-Point Inventory (Step 1)

All entry points are HTTP (ASP.NET Core Razor Pages handlers):

| Entry Point | Source |
|---|---|
| `GET /` | `IndexModel.OnGetAsync` |
| `POST /?handler=Upload` | `IndexModel.OnPostUploadAsync` |
| `GET /Detail/{id?}` | `DetailModel.OnGetAsync` |
| `POST /Detail/{id}?handler=Delete` | `DetailModel.OnPostDeleteAsync` |
| `GET /photo/{id:int}` | `PhotoFileModel.OnGetAsync` |
| `GET /Login` | `LoginModel.OnGet` |
| `POST /Login` | `LoginModel.OnPostAsync` |

Excluded as out-of-scope (no migrated-resource access, no behavior at risk): `ErrorModel.OnGet` (`/Error`), `Privacy.cshtml.cs` (static privacy page).

## Existing Test Inventory & Conventions (Step 2)

- `PhotoAlbum.Tests/Unit/Services/PhotoServiceTests.cs`: 7 xUnit tests exercising `PhotoService` directly (upload success/invalid-mime/oversized/empty-not-covered-directly, DB ordering, delete). These call the service layer, not the HTTP entry points, so per the skill's coverage rule they do **not** count toward entry-point coverage — every entry point above is covered fresh in `test-cases.md`.
- Conventions extracted and carried forward: xUnit + `Assert.*`, Arrange/Act/Assert comments, `FormFile` built over `MemoryStream` for upload fixtures, EF Core InMemory provider for the database. The test project already references `Microsoft.AspNetCore.Mvc.Testing`, enabling `WebApplicationFactory<Program>`-based HTTP-level tests in the verify phase.
- No existing IT/E2E tests were found (only unit tests), so the "Existing Test Coverage" table was omitted from `test-cases.md` per the template's instruction.

## Test Case Counts by Category (all new — no existing IT coverage)

| Entry Point | happy-path | boundary | special-input | failure | Total |
|---|---|---|---|---|---|
| `GET /` | 1 | 1 | — | 1 | 3 |
| `POST /?handler=Upload` | 1 | 2 | 3 | 4 | 10 |
| `GET /Detail/{id?}` | 1 | 2 | 1 | 1 | 5 |
| `POST /Detail/{id}?handler=Delete` | 1 | 1 | — | 2 | 4 |
| `GET /photo/{id:int}` | 1 | — | 1 | 3 | 5 |
| `GET /Login` | 1 | — | — | — (documented exception, no failure branch exists) | 1 |
| `POST /Login` | 1 | 1 | 1 | 2 | 5 |
| **Total** | **7** | **7** | **6** | **13** | **33** |

## `testdata/` File List

```
testdata/configuration/admin-credentials.json
testdata/inputs/corrupted.jpg
testdata/inputs/empty.jpg
testdata/inputs/not-an-image.pdf
testdata/inputs/oversized.bin
testdata/inputs/sample.gif
testdata/inputs/sample.jpg
testdata/inputs/sample.png
testdata/inputs/sample.webp
testdata/inputs/tiny-1x1.jpg
testdata/inputs/unicode-name-source.jpg
testdata/seed-data/three-photos.json
```

All files referenced from `test-cases.md` were verified to exist under `PhotoAlbum.Tests/test-cases/testdata/` (see validation run below). `sample.gif`, `sample.webp`, and `tiny-1x1.jpg` are provided as additional format-coverage fixtures available to the verify phase even though not all are cited by a specific test-case ID in the current spec revision.

## Infra Decision Table (Step 5)

Confirmed Integration Test Environment: **mock** (explicit in `.github/modernize/modernization-plan/plan.md` questionnaire and task `005-integrationTest` requirements).

| Dependency | Infra Match | Decision | Auth Method | Reason |
|---|---|---|---|---|
| Azure Blob Storage (photo storage container) | Yes — `infra/main.bicep`, but mock mode overrides | mock | n/a | Integration Test Environment confirmed `mock`. |
| Azure SQL Database (`Photos` table via EF Core) | Yes — `infra/main.bicep`, but mock mode overrides | mock | n/a | Integration Test Environment confirmed `mock`. |
| Azure App Configuration | Yes — `infra/main.bicep` (potentially), but mock mode overrides | mock | n/a | Integration Test Environment confirmed `mock`. |
| Azure Key Vault (optional/conditional) | No | mock | n/a | Not in the 3-dependency migration scope; conditional path left unexercised. |

Full detail: `PhotoAlbum.Tests/test-cases/infra-decision-table.md`.

## Freeze Validation (Step 6)

- All 33 test cases populate every required field (ID, Category, Entry Point Type, Entry Point, Trigger, Preconditions, Expected Response, Resource Verification, Negative Verification where applicable, Data References).
- No banned phrasings detected.
- Entry-point coverage gate satisfied for all entry points except the documented, justified exception at `GET /Login` (trivial no-branch handler with no migrated-resource access).
- All `testdata/...` paths referenced in `test-cases.md` verified to exist on disk.

## Build & Test Validation

```
dotnet build PhotoAlbum.sln  → Build succeeded, 0 Warnings, 0 Errors
dotnet test PhotoAlbum.sln   → Passed: 7, Failed: 0, Skipped: 0, Total: 7
```

Success criteria met: `passBuild=true`, `passUnitTests=true`.

## Frozen Artifacts

The following are now **FROZEN** and must not be modified by subsequent migration/verification phases:

- `PhotoAlbum.Tests/test-cases/test-cases.md`
- `PhotoAlbum.Tests/test-cases/infra-decision-table.md`
- `PhotoAlbum.Tests/test-cases/testdata/**`

Any required change forces a re-freeze cycle (unfreeze → amend → re-validate → re-freeze) — there is no side channel.
