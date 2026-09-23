# Modernization Plan: PhotoAlbum Assessment-Driven Azure Modernization

**Project**: PhotoAlbum

---

## Technical Framework

- **Language**: C#
- **Framework**: ASP.NET Core (Razor Pages), net10.0
- **Build Tool**: MSBuild / dotnet CLI
- **Database**: Azure SQL Database (Entity Framework Core, managed identity)
- **Key Dependencies**: Azure.Storage.Blobs, Azure.Identity, SixLabors.ImageSharp, Microsoft.EntityFrameworkCore.SqlServer

---

## Overview

> This migration addresses the cloud-readiness issues identified in the PhotoAlbum assessment report and completes the application's move to Azure. The application already targets a supported .NET LTS runtime and uses Azure Blob Storage and Azure SQL Database with managed identity for most operations, but it retains a local-disk fallback storage path with synchronous file I/O, and local-only application configuration. The new architecture will:
>
> - Remove remaining local file-based I/O and synchronous file APIs so photo storage relies solely on Azure Blob Storage
> - Externalize local application configuration to Azure App Configuration so settings are managed centrally instead of only in `appsettings.json`
> - Evaluate and modernize how static web assets are served so the app is fully container/cloud ready
> - Scan and remediate known CVEs in project dependencies
> - Deploy the application to the existing Azure Container Apps infrastructure already defined in the repository
>
> The migration reuses the existing Bicep/`azure.yaml` infrastructure and Dockerfile already present in the repository, verifies each change with mocked integration tests, and finishes with a security pass before deployment.

**Note on assessment findings**: The "Hardcoded sensitive data detected" issue (`PhotoAlbum/Pages/Login.cshtml.cs`) was verified against the current codebase and found to be a false positive — admin credentials are already read from configuration (`Admin:Username` / `Admin:Password`) with a fixed-time comparison, not hardcoded. This issue is excluded from the task list.

---

## Migration Impact Summary

| Application | Original Service | New Azure Service | Authentication | Comments |
|-------------|------------------|--------------------|-----------------|----------|
| PhotoAlbum  | Local disk fallback storage | Azure Blob Storage (only) | Managed Identity | Removes local IO/sync file APIs |
| PhotoAlbum  | Local `appsettings.json` values | Azure App Configuration | Managed Identity | Non-secret settings only |
| PhotoAlbum  | Local static wwwroot assets | Reviewed for CDN/Blob-backed hosting | Managed Identity (if applicable) | Optional, low-risk item |
| PhotoAlbum  | N/A | Azure Container Apps (existing infra) | Managed Identity | Reuses repo `infra/main.bicep`, `azure.yaml`, `Dockerfile` |

---

## Open Questions & Questionnaire

- [x] Q: Should the plan include environment/infrastructure provisioning? → A: No — reuse existing repo infra/config (Bicep, `azure.yaml`, `Dockerfile` targeting Azure Container Apps)
- [x] Q: Should the plan include integration testing to verify migrated services? → A: Yes — Mock mode (no live Azure infra required for tests)
- [x] Q: Should the plan include a security scan / CVE remediation task? → A: Yes — include security/CVE remediation
- [x] Q: Which Azure deployment target should the plan use? → A: Azure Container Apps (already defined in repo `azure.yaml`/`infra/main.bicep`; no separate containerization task needed)
