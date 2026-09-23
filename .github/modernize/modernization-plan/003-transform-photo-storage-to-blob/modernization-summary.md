# Azure Blob Storage migration summary

## Objective
Replace the app's local disk-based upload and retrieval flow with Azure Blob Storage-backed content delivery while keeping the gallery behavior stable and preserving validation and metadata capture.

## What changed
- Added Azure Blob Storage support via `Azure.Storage.Blobs` and `DefaultAzureCredential`.
- Registered a singleton `BlobServiceClient` and a storage abstraction used by the photo service.
- Removed direct writes to `wwwroot/uploads` and replaced them with blob uploads under a `photos/` key.
- Updated the photo file endpoint to read content from the configured storage backend instead of local files.
- Preserved image validation, file size limits, metadata capture, rollback cleanup, and delete behavior.
- Added compatibility for legacy `/uploads/...` paths so older persisted metadata can still resolve to the new blob layout.
- Updated configuration to use `Storage:ServiceUri`, `Storage:ContainerName`, and `Storage:AccountName` instead of the local upload-path setting.
- Kept a local fallback for non-Azure test/dev environments so unit tests remain deterministic without live Azure credentials.

## Files touched
- `PhotoAlbum/Program.cs`
- `PhotoAlbum/Services/PhotoService.cs`
- `PhotoAlbum/Services/PhotoBlobStorage.cs`
- `PhotoAlbum/Pages/PhotoFile.cshtml.cs`
- `PhotoAlbum/appsettings.json`
- `PhotoAlbum/PhotoAlbum.csproj`
- `PhotoAlbum/Models/Photo.cs`
- `PhotoAlbum.Tests/Unit/Services/PhotoServiceTests.cs`
- `CLAUDE.md`
- `PROJECT.md`
- `.gitignore`

## Validation
- `dotnet test PhotoAlbum.sln --nologo`
- Result: Passed (7/7 tests)

## Notes
- The app uses managed identity and Azure Blob Storage when valid Azure configuration is present.
- The fallback path keeps the gallery working in local/test environments without hard-coded upload directories.
