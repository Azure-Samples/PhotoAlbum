# Test Cases

> This document is the **frozen behavioral specification** of the `PhotoAlbum` module's external HTTP surface for the migration in scope. It is the source of truth that the `verify-test-baseline` skill uses to generate `*PostMigrationIT` tests against the new implementation. No test code is generated in the baseline phase.

## Metadata

| Field | Value |
|-------|-------|
| Project | PhotoAlbum |
| Module | `PhotoAlbum` (single ASP.NET Core Razor Pages web app; test source root `PhotoAlbum.Tests`) |
| Migration Scope | Remove remaining local file-based I/O / synchronous file APIs so photo storage relies solely on Azure Blob Storage; externalize local application configuration to Azure App Configuration; evaluate/modernize static web asset hosting; scan and remediate known CVEs; deploy to existing Azure Container Apps infrastructure. |
| Created At | 2026-09-23 |
| Status | baseline (frozen) |
| Testing Conventions | xUnit 2.x with `Assert.*` (Arrange/Act/Assert comment blocks), as used in `PhotoAlbum.Tests/Unit/Services/PhotoServiceTests.cs`. The test project already references `Microsoft.AspNetCore.Mvc.Testing` and `Microsoft.EntityFrameworkCore.InMemory`, so post-migration entry-point tests should use `WebApplicationFactory<Program>` for HTTP-level calls and an EF Core InMemory (or equivalent mocked) provider for the database, consistent with the existing test's DB setup style. `IFormFile` fixtures should be built the same way the existing test builds them (`FormFile` wrapping a `MemoryStream`), reading bytes from `testdata/inputs/*`. |

## General Preconditions (apply to every test case unless overridden)

- The host is started with configuration key `IsTestEnvironment = true` so `Program.cs` skips `context.Database.MigrateAsync()` on startup (see `PhotoAlbum/Program.cs`).
- All `POST` handlers (`/?handler=Upload`, `/Detail/{id}?handler=Delete`, `/Login`) are protected by ASP.NET Core's default antiforgery validation (`@Html.AntiForgeryToken()` / `FormTagHelper` auto-injection). A valid antiforgery cookie + hidden field pair, obtained from a prior `GET` to the same page, is a precondition for every `POST` test case below unless the case is specifically testing antiforgery rejection (not in scope here — antiforgery behavior itself is framework-supplied and unaffected by this migration).
- Unless a case states otherwise, the request is unauthenticated (no auth cookie).

## Entry-Point Inventory

| Entry Point | Type | Source (file:symbol) | Covered by |
|---|---|---|---|
| `GET /` | HTTP | `PhotoAlbum/Pages/Index.cshtml.cs:IndexModel.OnGetAsync` | TC-WEB-001, TC-WEB-002, TC-WEB-003 |
| `POST /?handler=Upload` | HTTP | `PhotoAlbum/Pages/Index.cshtml.cs:IndexModel.OnPostUploadAsync` | TC-WEB-004..TC-WEB-013 |
| `GET /Detail/{id?}` | HTTP | `PhotoAlbum/Pages/Detail.cshtml.cs:DetailModel.OnGetAsync` | TC-WEB-014..TC-WEB-018 |
| `POST /Detail/{id}?handler=Delete` | HTTP | `PhotoAlbum/Pages/Detail.cshtml.cs:DetailModel.OnPostDeleteAsync` | TC-WEB-019..TC-WEB-022 |
| `GET /photo/{id:int}` | HTTP | `PhotoAlbum/Pages/PhotoFile.cshtml.cs:PhotoFileModel.OnGetAsync` | TC-WEB-023..TC-WEB-027 |
| `GET /Login` | HTTP | `PhotoAlbum/Pages/Login.cshtml.cs:LoginModel.OnGet` | TC-WEB-028 |
| `POST /Login` | HTTP | `PhotoAlbum/Pages/Login.cshtml.cs:LoginModel.OnPostAsync` | TC-WEB-029..TC-WEB-033 |

Entry-point types used: `HTTP`.

**Coverage-gate note on `GET /Login` (TC-WEB-028):** `LoginModel.OnGet` (`public void OnGet() { }`) contains no branching logic and touches no migrated resource — it unconditionally renders the empty login form. No application-level failure path exists at this entry point (a framework-level failure, e.g. a crashed process, is out of scope for a behavior-preservation spec). This entry point is therefore intentionally covered by a single `happy-path` case only; this is a documented exception to the "happy-path + failure per entry point" rule, not an oversight.

**Out-of-scope pages:** `PhotoAlbum/Pages/Error.cshtml.cs` (`ErrorModel.OnGet`) and `PhotoAlbum/Pages/Privacy.cshtml.cs` are pure diagnostic/static pages that touch no migrated resource (no Blob Storage, SQL, or App Configuration access) and carry no behavior that this migration could change; they are excluded from the entry-point inventory per the module-scoping principle for resources not in migration scope.

## Existing Test Coverage

`PhotoAlbum.Tests/Unit/Services/PhotoServiceTests.cs` contains unit tests that call `PhotoService` methods directly (not through an HTTP entry point) and use a local-disk-backed `IPhotoBlobStorage` implementation. Per the coverage rule ("test cases that exercise only helpers called within an entry point do NOT count toward this requirement"), these do not satisfy the entry-point coverage gate; every entry point below is therefore covered by new test cases in this document. The existing test's fixture-building conventions (`FormFile` over `MemoryStream`, `Assert.*`) are reused for the new cases (see Testing Conventions above).

## Test Cases

### TC-WEB-001 Gallery listing — happy path

| Field | Value |
|-------|-------|
| ID | TC-WEB-001 |
| Category | happy-path |
| Entry Point Type | HTTP |
| Entry Point | `GET /` |
| Description | The gallery page lists all photos in the database ordered by upload date, newest first. |

**Trigger**

`GET /` with no query string, no body.

**Preconditions**

- Three rows exist in the `Photos` table, seeded as (in insertion order): `first.jpg` (`UploadedAt` = T-2h), `second.jpg` (`UploadedAt` = T-1h), `third.jpg` (`UploadedAt` = T-0h). Field values otherwise as in `testdata/seed-data/three-photos.json`.

**Expected Response**

- Status: `200 OK`.
- Body: HTML page whose photo gallery list contains exactly 3 entries in this exact order of `OriginalFileName`: `third.jpg`, `second.jpg`, `first.jpg` (newest first).

**Resource Verification**

- No rows are added to, removed from, or modified in the `Photos` table by this request (read-only operation).

**Negative Verification**

- None — pure happy-path read with no plausible side-effect-leak risk.

**Data References**

- `testdata/seed-data/three-photos.json`

---

### TC-WEB-002 Gallery listing — empty gallery (boundary)

| Field | Value |
|-------|-------|
| ID | TC-WEB-002 |
| Category | boundary |
| Entry Point Type | HTTP |
| Entry Point | `GET /` |
| Description | With no photos in the database, the gallery renders an empty list rather than erroring. |

**Trigger**

`GET /` with no query string, no body.

**Preconditions**

- The `Photos` table contains zero rows.

**Expected Response**

- Status: `200 OK`.
- Body: HTML page whose photo gallery list contains exactly 0 entries (the "No photos yet" empty-state content is present in the response body).

**Resource Verification**

- No rows are added to the `Photos` table.

**Negative Verification**

- None — pure happy-path/boundary read.

**Data References**

- None.

---

### TC-WEB-003 Gallery listing — data-store failure degrades to empty gallery (failure)

| Field | Value |
|-------|-------|
| ID | TC-WEB-003 |
| Category | failure |
| Entry Point Type | HTTP |
| Entry Point | `GET /` |
| Description | When the photo data store is unavailable, the page still renders successfully with an empty photo list instead of surfacing a server error, because `IndexModel.OnGetAsync` catches the exception. |

**Trigger**

`GET /` with no query string, no body.

**Preconditions**

- The photo metadata store is configured to throw on the query used by `GetAllPhotosAsync` (e.g. simulated database-unavailable condition).

**Expected Response**

- Status: `200 OK`.
- Body: HTML page whose photo gallery list contains exactly 0 entries (same empty-state content as TC-WEB-002).

**Resource Verification**

- None — the store is unavailable for this request; there is no resource state to observe beyond the empty rendered list already captured in Expected Response.

**Negative Verification**

- The response is NOT a `5xx` status code.

**Data References**

- None.

---

### TC-WEB-004 Upload — single valid JPEG (happy path)

| Field | Value |
|-------|-------|
| ID | TC-WEB-004 |
| Category | happy-path |
| Entry Point Type | HTTP |
| Entry Point | `POST /?handler=Upload` |
| Description | Uploading one valid JPEG image creates a photo record and returns its details in the JSON response. |

**Trigger**

`POST /?handler=Upload`, `Content-Type: multipart/form-data`, one form part named `files` containing `testdata/inputs/sample.jpg` (content-type `image/jpeg`, filename `sample.jpg`, 32x24 px).

**Preconditions**

- The `Photos` table contains zero rows.
- The blob/storage backend accepts writes.

**Expected Response**

- Status: `200 OK`.
- Body (JSON): `success = true`; `uploadedPhotos` has exactly 1 entry with `originalFileName = "sample.jpg"`, `width = 32`, `height = 24`, `fileSize` = byte length of `testdata/inputs/sample.jpg`, and a non-null `id`; `failedUploads` is an empty array.

**Resource Verification**

- Exactly one row is added to the `Photos` table with `OriginalFileName = "sample.jpg"`, `MimeType = "image/jpeg"`, `Width = 32`, `Height = 24`, `FileSize` = byte length of `testdata/inputs/sample.jpg`.
- One new object exists in the photo storage container whose content is byte-identical to `testdata/inputs/sample.jpg` and whose stored key is referenced by that row's `FilePath`.

**Negative Verification**

- None — pure happy-path upload with no plausible side-effect-leak risk.

**Data References**

- `testdata/inputs/sample.jpg`

---

### TC-WEB-005 Upload — multiple files, mixed valid/invalid (boundary)

| Field | Value |
|-------|-------|
| ID | TC-WEB-005 |
| Category | boundary |
| Entry Point Type | HTTP |
| Entry Point | `POST /?handler=Upload` |
| Description | A batch upload with one valid image and one invalid file partially succeeds: the valid file is stored and the invalid one is reported as failed, in the same response. |

**Trigger**

`POST /?handler=Upload`, `Content-Type: multipart/form-data`, two form parts named `files`: part 1 = `testdata/inputs/sample.png` (content-type `image/png`, filename `sample.png`), part 2 = `testdata/inputs/not-an-image.pdf` (content-type `application/pdf`, filename `not-an-image.pdf`).

**Preconditions**

- The `Photos` table contains zero rows.

**Expected Response**

- Status: `200 OK`.
- Body (JSON): `success = true`; `uploadedPhotos` has exactly 1 entry with `originalFileName = "sample.png"`; `failedUploads` has exactly 1 entry with `fileName = "not-an-image.pdf"` and `error = "File type not supported. Please upload JPEG, PNG, GIF, or WebP images."`.

**Resource Verification**

- Exactly one row is added to the `Photos` table, with `OriginalFileName = "sample.png"`.
- No row is added to the `Photos` table for `not-an-image.pdf`.

**Negative Verification**

- No object is written to the photo storage container for `not-an-image.pdf`.

**Data References**

- `testdata/inputs/sample.png`
- `testdata/inputs/not-an-image.pdf`

---

### TC-WEB-006 Upload — no files provided (boundary)

| Field | Value |
|-------|-------|
| ID | TC-WEB-006 |
| Category | boundary |
| Entry Point Type | HTTP |
| Entry Point | `POST /?handler=Upload` |
| Description | Submitting the upload handler with an empty file list is rejected before any processing occurs. |

**Trigger**

`POST /?handler=Upload`, `Content-Type: multipart/form-data`, with the `files` form field omitted entirely (no file parts in the body).

**Preconditions**

- None.

**Expected Response**

- Status: `400 Bad Request`.
- Body (JSON): `success = false`, `error = "No files provided"`.

**Resource Verification**

- None — request is rejected before any resource access.

**Negative Verification**

- No row is added to the `Photos` table.
- No object is written to the photo storage container.

**Data References**

- None.

---

### TC-WEB-007 Upload — unicode filename preserved (special-input)

| Field | Value |
|-------|-------|
| ID | TC-WEB-007 |
| Category | special-input |
| Entry Point Type | HTTP |
| Entry Point | `POST /?handler=Upload` |
| Description | A file uploaded with a Unicode filename is stored successfully and the original filename (including non-ASCII characters) is preserved verbatim in the response and database record. |

**Trigger**

`POST /?handler=Upload`, `Content-Type: multipart/form-data`, one form part named `files` containing the bytes of `testdata/inputs/unicode-name-source.jpg` (content-type `image/jpeg`), submitted with filename `Ünïcødé 名前 📷.jpg`.

**Preconditions**

- The `Photos` table contains zero rows.

**Expected Response**

- Status: `200 OK`.
- Body (JSON): `success = true`; `uploadedPhotos` has exactly 1 entry with `originalFileName = "Ünïcødé 名前 📷.jpg"`; `failedUploads` is an empty array.

**Resource Verification**

- Exactly one row is added to the `Photos` table with `OriginalFileName = "Ünïcødé 名前 📷.jpg"` (exact Unicode string preserved, no mangling/transliteration).

**Negative Verification**

- None — pure happy-path variant with no plausible side-effect-leak risk beyond filename fidelity, already asserted above.

**Data References**

- `testdata/inputs/unicode-name-source.jpg`

---

### TC-WEB-008 Upload — oversized file rejected (special-input)

| Field | Value |
|-------|-------|
| ID | TC-WEB-008 |
| Category | special-input |
| Entry Point Type | HTTP |
| Entry Point | `POST /?handler=Upload` |
| Description | A single file larger than the configured 10 MB limit (`FileUpload:MaxFileSizeBytes = 10485760`) is rejected with a size-limit error before any image-format inspection occurs. |

**Trigger**

`POST /?handler=Upload`, `Content-Type: multipart/form-data`, one form part named `files` containing `testdata/inputs/oversized.bin` (11534336 bytes, content-type `image/jpeg`, filename `oversized.bin`).

**Preconditions**

- The `Photos` table contains zero rows.

**Expected Response**

- Status: `200 OK`.
- Body (JSON): `success = false`; `uploadedPhotos` is an empty array; `failedUploads` has exactly 1 entry with `fileName = "oversized.bin"` and `error = "File size exceeds 10MB limit."`.

**Resource Verification**

- None — rejected before any resource write.

**Negative Verification**

- No row is added to the `Photos` table.
- No object is written to the photo storage container.

**Data References**

- `testdata/inputs/oversized.bin`

---

### TC-WEB-009 Upload — zero-byte file rejected (special-input)

| Field | Value |
|-------|-------|
| ID | TC-WEB-009 |
| Category | special-input |
| Entry Point Type | HTTP |
| Entry Point | `POST /?handler=Upload` |
| Description | An empty (0-byte) file is rejected with a specific "file is empty" error. |

**Trigger**

`POST /?handler=Upload`, `Content-Type: multipart/form-data`, one form part named `files` containing `testdata/inputs/empty.jpg` (0 bytes, content-type `image/jpeg`, filename `empty.jpg`).

**Preconditions**

- The `Photos` table contains zero rows.

**Expected Response**

- Status: `200 OK`.
- Body (JSON): `success = false`; `uploadedPhotos` is an empty array; `failedUploads` has exactly 1 entry with `fileName = "empty.jpg"` and `error = "File is empty."`.

**Resource Verification**

- None — rejected before any resource write.

**Negative Verification**

- No row is added to the `Photos` table.
- No object is written to the photo storage container.

**Data References**

- `testdata/inputs/empty.jpg`

---

### TC-WEB-010 Upload — non-image content rejected (failure)

| Field | Value |
|-------|-------|
| ID | TC-WEB-010 |
| Category | failure |
| Entry Point Type | HTTP |
| Entry Point | `POST /?handler=Upload` |
| Description | A non-image file (PDF) is rejected because it fails content-based image-format detection, regardless of its declared content-type. |

**Trigger**

`POST /?handler=Upload`, `Content-Type: multipart/form-data`, one form part named `files` containing `testdata/inputs/not-an-image.pdf` (content-type `application/pdf`, filename `not-an-image.pdf`).

**Preconditions**

- The `Photos` table contains zero rows.

**Expected Response**

- Status: `200 OK`.
- Body (JSON): `success = false`; `uploadedPhotos` is an empty array; `failedUploads` has exactly 1 entry with `fileName = "not-an-image.pdf"` and `error = "File type not supported. Please upload JPEG, PNG, GIF, or WebP images."`.

**Resource Verification**

- None — rejected before any resource write.

**Negative Verification**

- No row is added to the `Photos` table.
- No object is written to the photo storage container.

**Data References**

- `testdata/inputs/not-an-image.pdf`

---

### TC-WEB-011 Upload — corrupted/undecodable image bytes rejected (failure)

| Field | Value |
|-------|-------|
| ID | TC-WEB-011 |
| Category | failure |
| Entry Point Type | HTTP |
| Entry Point | `POST /?handler=Upload` |
| Description | A file with a `.jpg` extension whose bytes are not a decodable image is rejected the same way as a wrong-type file, because format detection is content-based, not extension-based. |

**Trigger**

`POST /?handler=Upload`, `Content-Type: multipart/form-data`, one form part named `files` containing `testdata/inputs/corrupted.jpg` (2048 bytes of non-image random data, content-type `image/jpeg`, filename `corrupted.jpg`).

**Preconditions**

- The `Photos` table contains zero rows.

**Expected Response**

- Status: `200 OK`.
- Body (JSON): `success = false`; `uploadedPhotos` is an empty array; `failedUploads` has exactly 1 entry with `fileName = "corrupted.jpg"` and `error = "File type not supported. Please upload JPEG, PNG, GIF, or WebP images."`.

**Resource Verification**

- None — rejected before any resource write.

**Negative Verification**

- No row is added to the `Photos` table.
- No object is written to the photo storage container.

**Data References**

- `testdata/inputs/corrupted.jpg`

---

### TC-WEB-012 Upload — storage backend write failure (failure)

| Field | Value |
|-------|-------|
| ID | TC-WEB-012 |
| Category | failure |
| Entry Point Type | HTTP |
| Entry Point | `POST /?handler=Upload` |
| Description | If the underlying photo storage backend fails while writing the uploaded content, the upload is reported as failed with a generic save error and no database record is created. |

**Trigger**

`POST /?handler=Upload`, `Content-Type: multipart/form-data`, one form part named `files` containing `testdata/inputs/sample.jpg` (content-type `image/jpeg`, filename `sample.jpg`).

**Preconditions**

- The `Photos` table contains zero rows.
- The photo storage backend is configured to throw on the write call used to persist new photo content.

**Expected Response**

- Status: `200 OK`.
- Body (JSON): `success = false`; `uploadedPhotos` is an empty array; `failedUploads` has exactly 1 entry with `fileName = "sample.jpg"` and `error = "Error saving file. Please try again."`.

**Resource Verification**

- None (the write failed) — there is no successfully written resource to observe.

**Negative Verification**

- No row is added to the `Photos` table.

**Data References**

- `testdata/inputs/sample.jpg`

---

### TC-WEB-013 Upload — database save failure triggers storage rollback (failure)

| Field | Value |
|-------|-------|
| ID | TC-WEB-013 |
| Category | failure |
| Entry Point Type | HTTP |
| Entry Point | `POST /?handler=Upload` |
| Description | If the photo content is written to storage successfully but the subsequent metadata save to the database fails, the application deletes the just-written storage object (rollback) and reports the upload as failed. |

**Trigger**

`POST /?handler=Upload`, `Content-Type: multipart/form-data`, one form part named `files` containing `testdata/inputs/sample.jpg` (content-type `image/jpeg`, filename `sample.jpg`).

**Preconditions**

- The `Photos` table contains zero rows.
- The photo storage backend accepts the write, but the subsequent database save (`SaveChangesAsync` persisting the new `Photos` row) is configured to throw.

**Expected Response**

- Status: `200 OK`.
- Body (JSON): `success = false`; `uploadedPhotos` is an empty array; `failedUploads` has exactly 1 entry with `fileName = "sample.jpg"` and `error = "Error saving photo information. Please try again."`.

**Resource Verification**

- None — the only object written to storage during this request is removed as part of rollback (see Negative Verification).

**Negative Verification**

- No row exists in the `Photos` table referencing this upload attempt.
- The storage object that was written for this upload attempt no longer exists after the request completes (deleted by the rollback path).

**Data References**

- `testdata/inputs/sample.jpg`

---

### TC-WEB-014 Photo detail — happy path with both navigation neighbors

| Field | Value |
|-------|-------|
| ID | TC-WEB-014 |
| Category | happy-path |
| Entry Point Type | HTTP |
| Entry Point | `GET /Detail/{id?}` |
| Description | Requesting an existing photo that has both a newer and an older neighbor renders its details and both navigation links. |

**Trigger**

`GET /Detail/{id}` where `{id}` is the database-assigned id of `second.jpg` from the 3-photo seed set.

**Preconditions**

- Same 3-row seed as TC-WEB-001 (`testdata/seed-data/three-photos.json`): `first.jpg` (oldest), `second.jpg` (middle), `third.jpg` (newest).

**Expected Response**

- Status: `200 OK`.
- Body: HTML page displaying `OriginalFileName = "second.jpg"`; a "Previous Photo" link targeting the id of `first.jpg` (older); a "Next Photo" link targeting the id of `third.jpg` (newer).

**Resource Verification**

- No rows are added to, removed from, or modified in the `Photos` table.

**Negative Verification**

- None — pure happy-path read.

**Data References**

- `testdata/seed-data/three-photos.json`

---

### TC-WEB-015 Photo detail — newest photo has no "next" neighbor (boundary)

| Field | Value |
|-------|-------|
| ID | TC-WEB-015 |
| Category | boundary |
| Entry Point Type | HTTP |
| Entry Point | `GET /Detail/{id?}` |
| Description | The most recently uploaded photo has a "previous" (older) neighbor but no "next" (newer) neighbor. |

**Trigger**

`GET /Detail/{id}` where `{id}` is the database-assigned id of `third.jpg` from the 3-photo seed set.

**Preconditions**

- Same 3-row seed as TC-WEB-001.

**Expected Response**

- Status: `200 OK`.
- Body: HTML page displaying `OriginalFileName = "third.jpg"`; a "Previous Photo" link targeting the id of `second.jpg`; no "Next Photo" link present.

**Resource Verification**

- No rows are added to, removed from, or modified in the `Photos` table.

**Negative Verification**

- None — pure boundary read.

**Data References**

- `testdata/seed-data/three-photos.json`

---

### TC-WEB-016 Photo detail — oldest photo has no "previous" neighbor (boundary)

| Field | Value |
|-------|-------|
| ID | TC-WEB-016 |
| Category | boundary |
| Entry Point Type | HTTP |
| Entry Point | `GET /Detail/{id?}` |
| Description | The oldest photo has a "next" (newer) neighbor but no "previous" (older) neighbor. |

**Trigger**

`GET /Detail/{id}` where `{id}` is the database-assigned id of `first.jpg` from the 3-photo seed set.

**Preconditions**

- Same 3-row seed as TC-WEB-001.

**Expected Response**

- Status: `200 OK`.
- Body: HTML page displaying `OriginalFileName = "first.jpg"`; a "Next Photo" link targeting the id of `second.jpg`; no "Previous Photo" link present.

**Resource Verification**

- No rows are added to, removed from, or modified in the `Photos` table.

**Negative Verification**

- None — pure boundary read.

**Data References**

- `testdata/seed-data/three-photos.json`

---

### TC-WEB-017 Photo detail — missing route id (special-input)

| Field | Value |
|-------|-------|
| ID | TC-WEB-017 |
| Category | special-input |
| Entry Point Type | HTTP |
| Entry Point | `GET /Detail/{id?}` |
| Description | Requesting `/Detail` with no id segment returns Not Found rather than rendering a page. |

**Trigger**

`GET /Detail` (no id route segment).

**Preconditions**

- None.

**Expected Response**

- Status: `404 Not Found`.

**Resource Verification**

- None — no resource is queried when the id is absent.

**Negative Verification**

- No query is issued against the `Photos` table.

**Data References**

- None.

---

### TC-WEB-018 Photo detail — id does not exist (failure)

| Field | Value |
|-------|-------|
| ID | TC-WEB-018 |
| Category | failure |
| Entry Point Type | HTTP |
| Entry Point | `GET /Detail/{id?}` |
| Description | Requesting a photo id that has no matching row returns Not Found. |

**Trigger**

`GET /Detail/999999` (an id guaranteed not to exist).

**Preconditions**

- The `Photos` table contains zero rows, or in any case no row with id `999999`.

**Expected Response**

- Status: `404 Not Found`.

**Resource Verification**

- None — no matching resource exists.

**Negative Verification**

- No row is added to, removed from, or modified in the `Photos` table.

**Data References**

- None.

---

### TC-WEB-019 Delete photo — authenticated admin deletes existing photo (happy path)

| Field | Value |
|-------|-------|
| ID | TC-WEB-019 |
| Category | happy-path |
| Entry Point Type | HTTP |
| Entry Point | `POST /Detail/{id}?handler=Delete` |
| Description | An authenticated admin deleting an existing photo removes both its database row and its stored object, then redirects to the gallery. |

**Trigger**

`POST /Detail/{id}?handler=Delete` where `{id}` is the database-assigned id of a previously-uploaded photo (e.g. via TC-WEB-004's fixture `testdata/inputs/sample.jpg`). Request carries a valid admin authentication cookie (see TC-WEB-029) and a valid antiforgery token pair.

**Preconditions**

- A row exists in the `Photos` table for the target id, with a corresponding object in the photo storage container.
- The caller is authenticated as the admin identity (see `testdata/configuration/admin-credentials.json` and TC-WEB-029).

**Expected Response**

- Status: `302 Found`, redirecting to `/` (the gallery page, `RedirectToPage("/Index")`).

**Resource Verification**

- The row with this id no longer exists in the `Photos` table.

**Negative Verification**

- The storage object previously referenced by the deleted row's `FilePath` no longer exists in the photo storage container.
- No other row in the `Photos` table is affected.

**Data References**

- `testdata/inputs/sample.jpg`

---

### TC-WEB-020 Delete photo — nonexistent id is a no-op redirect (boundary)

| Field | Value |
|-------|-------|
| ID | TC-WEB-020 |
| Category | boundary |
| Entry Point Type | HTTP |
| Entry Point | `POST /Detail/{id}?handler=Delete` |
| Description | Deleting an id that does not exist still redirects to the gallery (the service call returns `false` rather than throwing), and produces no visible error. |

**Trigger**

`POST /Detail/999999?handler=Delete` with a valid admin authentication cookie and a valid antiforgery token pair.

**Preconditions**

- The `Photos` table contains no row with id `999999`.
- The caller is authenticated as the admin identity.

**Expected Response**

- Status: `302 Found`, redirecting to `/` (`RedirectToPage("/Index")`).

**Resource Verification**

- None — there was no matching row or object to begin with.

**Negative Verification**

- No row is added to, removed from, or modified in the `Photos` table.
- No object is deleted from the photo storage container.

**Data References**

- None.

---

### TC-WEB-021 Delete photo — unauthenticated request is challenged (failure)

| Field | Value |
|-------|-------|
| ID | TC-WEB-021 |
| Category | failure |
| Entry Point Type | HTTP |
| Entry Point | `POST /Detail/{id}?handler=Delete` |
| Description | An unauthenticated caller attempting to delete a photo is challenged (redirected to the login page) and no deletion occurs (CWE-306 protection). |

**Trigger**

`POST /Detail/{id}?handler=Delete` where `{id}` is the id of an existing photo, with no authentication cookie present, and a valid antiforgery token pair.

**Preconditions**

- A row exists in the `Photos` table for the target id, with a corresponding object in the photo storage container.
- No authentication cookie is present on the request.

**Expected Response**

- Status: `302 Found`, redirecting to a URL whose path is `/Login` and whose query string includes a `ReturnUrl` parameter referencing the original request path (`/Detail/{id}`).

**Resource Verification**

- The row for the target id still exists in the `Photos` table, unchanged.
- The corresponding object still exists in the photo storage container.

**Negative Verification**

- No row is removed from or modified in the `Photos` table.
- No object is deleted from the photo storage container.

**Data References**

- None.

---

### TC-WEB-022 Delete photo — deletion failure surfaces error and preserves data (failure)

| Field | Value |
|-------|-------|
| ID | TC-WEB-022 |
| Category | failure |
| Entry Point Type | HTTP |
| Entry Point | `POST /Detail/{id}?handler=Delete` |
| Description | If the delete operation throws (e.g. the data store is unavailable), the admin is redirected back to the photo's detail page with a visible error message, and no data is removed. |

**Trigger**

`POST /Detail/{id}?handler=Delete` where `{id}` is the id of an existing photo, with a valid admin authentication cookie and a valid antiforgery token pair.

**Preconditions**

- A row exists in the `Photos` table for the target id.
- The delete path (database row removal or storage object removal) is configured to throw for this request.
- The caller is authenticated as the admin identity.

**Expected Response**

- Status: `302 Found`, redirecting to `/Detail/{id}` (`RedirectToPage("/Detail", new { id })`).
- The redirected-to page's `TempData["Error"]` equals `"Failed to delete photo. Please try again."` (observable as the alert text on the immediately-following `GET /Detail/{id}` response).

**Resource Verification**

- The row for the target id still exists in the `Photos` table, unchanged.

**Negative Verification**

- No row is removed from the `Photos` table.

**Data References**

- None.

---

### TC-WEB-023 Serve photo file — happy path

| Field | Value |
|-------|-------|
| ID | TC-WEB-023 |
| Category | happy-path |
| Entry Point Type | HTTP |
| Entry Point | `GET /photo/{id:int}` |
| Description | Requesting an existing photo's file returns its bytes with the correct MIME type and caching headers. |

**Trigger**

`GET /photo/{id}` where `{id}` is the database-assigned id of a photo uploaded from `testdata/inputs/sample.jpg`.

**Preconditions**

- A row exists in the `Photos` table for the target id with `MimeType = "image/jpeg"`, and a corresponding object exists in the photo storage container with content byte-identical to `testdata/inputs/sample.jpg`.

**Expected Response**

- Status: `200 OK`.
- Headers: `Content-Type: image/jpeg`; `Cache-Control: public,max-age=31536000`; `ETag` present and non-empty.
- Body: bytes identical to `testdata/inputs/sample.jpg`.

**Resource Verification**

- No rows are added to, removed from, or modified in the `Photos` table (read-only operation).

**Negative Verification**

- None — pure happy-path read.

**Data References**

- `testdata/inputs/sample.jpg`

---

### TC-WEB-024 Serve photo file — non-numeric id does not route (special-input)

| Field | Value |
|-------|-------|
| ID | TC-WEB-024 |
| Category | special-input |
| Entry Point Type | HTTP |
| Entry Point | `GET /photo/{id:int}` |
| Description | A non-integer path segment fails the route's `{id:int}` constraint and never reaches the page handler, resulting in Not Found at the routing layer. |

**Trigger**

`GET /photo/abc` (non-numeric id segment).

**Preconditions**

- None.

**Expected Response**

- Status: `404 Not Found`.

**Resource Verification**

- None — routing rejects the request before any handler code runs; no resource is queried.

**Negative Verification**

- No query is issued against the `Photos` table.
- No read is issued against the photo storage container.

**Data References**

- None.

---

### TC-WEB-025 Serve photo file — id not in database (failure)

| Field | Value |
|-------|-------|
| ID | TC-WEB-025 |
| Category | failure |
| Entry Point Type | HTTP |
| Entry Point | `GET /photo/{id:int}` |
| Description | Requesting a numeric id with no matching database row returns Not Found without attempting a storage read. |

**Trigger**

`GET /photo/999999` (an id guaranteed not to exist).

**Preconditions**

- The `Photos` table contains no row with id `999999`.

**Expected Response**

- Status: `404 Not Found`.

**Resource Verification**

- None — no matching row exists.

**Negative Verification**

- No read is issued against the photo storage container for this id.

**Data References**

- None.

---

### TC-WEB-026 Serve photo file — database row exists but storage object is missing (failure)

| Field | Value |
|-------|-------|
| ID | TC-WEB-026 |
| Category | failure |
| Entry Point Type | HTTP |
| Entry Point | `GET /photo/{id:int}` |
| Description | If a `Photos` row references a storage object that does not exist (or has been removed out of band), the request returns Not Found rather than an error. |

**Trigger**

`GET /photo/{id}` where `{id}` is the id of a `Photos` row whose referenced storage object does not exist.

**Preconditions**

- A row exists in the `Photos` table for the target id, but no corresponding object exists in the photo storage container for the path it references.

**Expected Response**

- Status: `404 Not Found`.

**Resource Verification**

- The `Photos` row for the target id is unchanged.

**Negative Verification**

- The `Photos` row for the target id is not deleted as a side effect of the missing storage object.

**Data References**

- None.

---

### TC-WEB-027 Serve photo file — unexpected storage error (failure)

| Field | Value |
|-------|-------|
| ID | TC-WEB-027 |
| Category | failure |
| Entry Point Type | HTTP |
| Entry Point | `GET /photo/{id:int}` |
| Description | If the storage backend fails with an error other than "not found" while reading a photo's bytes, the request surfaces a generic server error. |

**Trigger**

`GET /photo/{id}` where `{id}` is the id of an existing `Photos` row.

**Preconditions**

- A row exists in the `Photos` table for the target id.
- The storage backend is configured to throw a non-"not found" error on the read call for this photo's object.

**Expected Response**

- Status: `500`.

**Resource Verification**

- The `Photos` row for the target id is unchanged.

**Negative Verification**

- The `Photos` row for the target id is not deleted as a side effect of the storage error.

**Data References**

- None.

---

### TC-WEB-028 Login page — renders empty form (happy path)

| Field | Value |
|-------|-------|
| ID | TC-WEB-028 |
| Category | happy-path |
| Entry Point Type | HTTP |
| Entry Point | `GET /Login` |
| Description | Requesting the login page renders the login form with no error message. |

**Trigger**

`GET /Login` with no query string.

**Preconditions**

- None.

**Expected Response**

- Status: `200 OK`.
- Body: HTML page containing a login form (`Username` and `Password` fields) with no error message rendered.

**Resource Verification**

- None — no external resource is touched by this handler.

**Negative Verification**

- None — see coverage-gate note in the Entry-Point Inventory section for why this entry point has no failure case.

**Data References**

- None.

---

### TC-WEB-029 Login — correct credentials sign in and redirect (happy path)

| Field | Value |
|-------|-------|
| ID | TC-WEB-029 |
| Category | happy-path |
| Entry Point Type | HTTP |
| Entry Point | `POST /Login` |
| Description | Submitting the configured admin username and password signs the caller in (auth cookie issued) and redirects to the gallery. |

**Trigger**

`POST /Login`, form-urlencoded body with `Username` and `Password` matching the values in `testdata/configuration/admin-credentials.json`, and `ReturnUrl` empty.

**Preconditions**

- The application is configured with `Admin:Username` / `Admin:Password` equal to the values in `testdata/configuration/admin-credentials.json`.

**Expected Response**

- Status: `302 Found`, redirecting to `/` (`RedirectToPage("/Index")`).
- Response sets an authentication cookie (`Set-Cookie` header present for the cookie-auth scheme).

**Resource Verification**

- None — login does not touch the `Photos` table or photo storage.

**Negative Verification**

- None — pure happy-path with no plausible side-effect-leak risk.

**Data References**

- `testdata/configuration/admin-credentials.json`

---

### TC-WEB-030 Login — valid local ReturnUrl honored (boundary)

| Field | Value |
|-------|-------|
| ID | TC-WEB-030 |
| Category | boundary |
| Entry Point Type | HTTP |
| Entry Point | `POST /Login` |
| Description | When a valid same-site `ReturnUrl` is supplied together with correct credentials, the post-login redirect goes to that URL instead of the default gallery page. |

**Trigger**

`POST /Login`, form-urlencoded body with `Username` / `Password` matching `testdata/configuration/admin-credentials.json`, and `ReturnUrl = /Detail/1`.

**Preconditions**

- Same admin configuration as TC-WEB-029.

**Expected Response**

- Status: `302 Found`, redirecting to `/Detail/1`.

**Resource Verification**

- None.

**Negative Verification**

- None — pure boundary variant of the happy path.

**Data References**

- `testdata/configuration/admin-credentials.json`

---

### TC-WEB-031 Login — external ReturnUrl is ignored (special-input)

| Field | Value |
|-------|-------|
| ID | TC-WEB-031 |
| Category | special-input |
| Entry Point Type | HTTP |
| Entry Point | `POST /Login` |
| Description | An external (non-local) `ReturnUrl` is rejected by `Url.IsLocalUrl` and the caller is redirected to the default gallery page instead, preventing open-redirect abuse. |

**Trigger**

`POST /Login`, form-urlencoded body with `Username` / `Password` matching `testdata/configuration/admin-credentials.json`, and `ReturnUrl = https://evil.example.com/phish`.

**Preconditions**

- Same admin configuration as TC-WEB-029.

**Expected Response**

- Status: `302 Found`, redirecting to `/` (`RedirectToPage("/Index")`) — NOT to `https://evil.example.com/phish`.

**Resource Verification**

- None.

**Negative Verification**

- The response's redirect target is not `https://evil.example.com/phish` or any external host.

**Data References**

- `testdata/configuration/admin-credentials.json`

---

### TC-WEB-032 Login — wrong password rejected (failure)

| Field | Value |
|-------|-------|
| ID | TC-WEB-032 |
| Category | failure |
| Entry Point Type | HTTP |
| Entry Point | `POST /Login` |
| Description | Submitting the correct username with an incorrect password is rejected with a generic invalid-credentials message and no auth cookie is issued. |

**Trigger**

`POST /Login`, form-urlencoded body with `Username` matching `testdata/configuration/admin-credentials.json` and `Password = "wrong-password-value"`.

**Preconditions**

- Same admin configuration as TC-WEB-029.

**Expected Response**

- Status: `200 OK`.
- Body: HTML page containing the error message `"Invalid username or password."`.
- No authentication cookie is set on the response.

**Resource Verification**

- None.

**Negative Verification**

- No authentication cookie is issued.

**Data References**

- `testdata/configuration/admin-credentials.json`

---

### TC-WEB-033 Login — admin not configured (failure)

| Field | Value |
|-------|-------|
| ID | TC-WEB-033 |
| Category | failure |
| Entry Point Type | HTTP |
| Entry Point | `POST /Login` |
| Description | If the application has no `Admin:Username` / `Admin:Password` configured, login fails closed with a dedicated message, regardless of submitted credentials. |

**Trigger**

`POST /Login`, form-urlencoded body with `Username = "admin"` and `Password = "any-value"`.

**Preconditions**

- The application configuration has no value set for `Admin:Username` and/or `Admin:Password` (both empty/absent).

**Expected Response**

- Status: `200 OK`.
- Body: HTML page containing the error message `"Admin account is not configured."`.
- No authentication cookie is set on the response.

**Resource Verification**

- None.

**Negative Verification**

- No authentication cookie is issued.

**Data References**

- None.

---

## Required Field Checklist (Freeze Gate)

- [x] **ID** unique, formatted `TC-WEB-<NNN>`.
- [x] **Category** is one of `happy-path`, `boundary`, `special-input`, `failure`.
- [x] **Entry Point Type** is `HTTP` for every case, consistent with the module's nature.
- [x] **Entry Point** is an exact production-code identifier (HTTP method + route, matching `@page` directives in the `.cshtml` files).
- [x] **Trigger** is concrete: method, path, content-type, form field names, and file references are all specified.
- [x] **Preconditions** enumerated (or `none`); all referenced seed data exists under `testdata/`.
- [x] **Expected Response** specifies exact status code and, where applicable, exact body/header content — no vague phrasing.
- [x] **Resource Verification** has at least one bullet or explicit `none` with justification, phrased in resource-neutral terms (`Photos` table row / storage container object, no SDK types).
- [x] **Negative Verification** present for every `failure` case and every boundary/no-op case with a plausible side-effect-leak risk.
- [x] **Data References** complete; every path exists under `testdata/` (verified below).

## Coverage Gate

- [x] Every entry point in the Entry-Point Inventory appears as the `Entry Point` of at least one `happy-path` test case, except `GET /Login` (documented exception — see Entry-Point Inventory note).
- [x] Every entry point in the Entry-Point Inventory appears as the `Entry Point` of at least one `failure` test case, except `GET /Login` (documented exception).
- [x] Each entry point covers all four buckets where applicable (see per-entry-point ID ranges in the inventory table).
- [x] Entity examples use 2–5 representative records (the 3-photo seed set `testdata/seed-data/three-photos.json`).

## Banned Phrasings — Self-Check

No instances of "should return a reasonable error", "behaves as expected", "approximately N items", "etc.", or unqualified "data is persisted" appear in this document. All resource checks name the concrete resource (`Photos` table row, photo storage container object) and the exact field-level state.
