---
name: azure-blob-migration
description: >-
  Migrate PhotoAlbum photo uploads from local disk to Azure Blob Storage on
  Azure Container Apps. WHEN: move uploaded files to Azure Blob, configure
  managed identity for blob access, or prepare PhotoAlbum storage for Azure.
  NOT for: .NET Framework upgrades, unrelated Azure infrastructure, or
  frontend redesigns.
---

# PhotoAlbum Local Storage → Azure Blob Storage

## Purpose

Move photo content from the app's local filesystem to Azure Blob Storage while
keeping SQL photo metadata, local development, and the existing gallery
behavior intact. The app already targets .NET 9.

## Preconditions

- The user has approved the assessment and migration plan.
- Confirm actual configuration and deployment conventions in the repository
  before changing them.
- Do not provision or deploy Azure resources unless the user explicitly asks.

## Transformation rules

| Existing behavior or configuration | Azure-ready approach | Requirements |
|---|---|---|
| `PhotoService` reads and writes files using local paths and `FileStream` | Put file operations behind a storage abstraction with local and Blob implementations | Keep upload validation and SQL metadata behavior; clean up stored blobs if metadata persistence fails |
| `PhotoFile` resolves a physical path under the app directory | Read the object through the storage abstraction and stream it to the response | Preserve content type, caching behavior, and not-found handling; avoid loading large files fully into memory |
| `FileUpload:UploadPath` selects local storage | Configure `AzureStorageBlob:Endpoint` and `AzureStorageBlob:ContainerName` for Azure; retain the local path for development/tests | Do not put account keys or connection secrets in source control |
| App identity accesses Azure Storage | Use the Container App managed identity with `DefaultAzureCredential` and the minimum Blob data role required | Do not enable public blob access unless explicitly required and approved |
| Azure deployment sets environment variables | Keep the deployed setting names aligned with application configuration | Validate that the selected container exists and configuration failures are visible |

## Procedure

1. Re-read the photo service, file-serving page, dependency registrations,
   tests, app settings, Dockerfile, and deployment scripts.
2. Implement the storage abstraction and both local and Blob implementations.
   Keep local storage as the default when no Blob endpoint is configured.
3. Register Blob access with managed identity; never embed storage credentials.
4. Add or update tests for local storage, Blob behavior where practical,
   upload rollback, deletion, and photo retrieval.
5. Run the focused tests and solution build. Report exact commands and results.
6. Document any Azure role assignments or settings required for deployment;
   do not create Azure resources without explicit user authorization.

## Verification checklist

- [ ] Local filesystem storage remains usable without Azure configuration.
- [ ] Azure configuration uses managed identity and private blobs.
- [ ] Upload, retrieval, deletion, and database-failure cleanup are covered.
- [ ] Relevant tests pass and the solution builds.
- [ ] Deployment prerequisites and remaining cloud-only checks are reported
  separately from locally verified results.
