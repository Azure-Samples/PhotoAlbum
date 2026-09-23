# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PhotoAlbum is an ASP.NET Core 9.0 Razor Pages application for photo gallery management. It's designed as a demo for GitHub Copilot app modernization, showcasing migration from local file storage to Azure Blob Storage.

**Tech Stack:**
- ASP.NET Core 9.0 (Razor Pages)
- Entity Framework Core 9.0 with Azure SQL Database and managed identity
- SixLabors.ImageSharp for image processing
- xUnit for testing

## Build and Run

**Build the solution:**
```bash
dotnet build PhotoAlbum.sln
```

**Run the application:**
```bash
dotnet run --project PhotoAlbum/PhotoAlbum.csproj
```

**Run all tests:**
```bash
dotnet test PhotoAlbum.Tests/PhotoAlbum.Tests.csproj
```

**Run specific test:**
```bash
dotnet test PhotoAlbum.Tests/PhotoAlbum.Tests.csproj --filter "FullyQualifiedName~TestMethodName"
```

**Database migrations:**
```bash
# Add new migration
dotnet ef migrations add MigrationName --project PhotoAlbum/PhotoAlbum.csproj

# Apply migrations
dotnet ef database update --project PhotoAlbum/PhotoAlbum.csproj
```

## Architecture

**Core Components:**

1. **PhotoService (PhotoAlbum/Services/PhotoService.cs)** - Service layer handling photo operations:
   - File upload with validation (size, MIME type)
   - Image dimension extraction via ImageSharp
   - Blob-backed storage via Azure Blob Storage (`DefaultAzureCredential`)
   - Database persistence with rollback cleanup on failure
   - Photo retrieval and deletion

2. **PhotoAlbumContext (PhotoAlbum/Data/PhotoAlbumContext.cs)** - EF Core DbContext:
   - Single `Photos` DbSet
   - Descending index on `UploadedAt` for chronological queries

3. **Photo Model (PhotoAlbum/Models/Photo.cs)** - Represents uploaded photos with:
   - Dual filename tracking (original + stored GUID name)
   - File metadata (size, MIME type, dimensions)
   - UTC timestamps

4. **Razor Pages (PhotoAlbum/Pages/):**
   - `Index.cshtml` - Gallery grid view with upload functionality
   - `Detail.cshtml` - Full-size photo display with metadata and navigation
   - `PhotoFile.cshtml` - File retrieval endpoint

**Configuration (appsettings.json):**
- `ConnectionStrings:DefaultConnection` - Azure SQL Database connection using managed identity
- `FileUpload:MaxFileSizeBytes` - 10MB limit
- `FileUpload:AllowedMimeTypes` - JPEG, PNG, GIF, WebP
- `Storage:ServiceUri` / `Storage:AccountName` - Azure Blob Storage endpoint
- `Storage:ContainerName` - Blob container for photos

**Startup (Program.cs):**
- Registers a singleton `BlobServiceClient` with `DefaultAzureCredential`
- Auto-applies EF migrations in development (skips in test environment)
- Configures form options for 10MB file uploads
- Registers `IPhotoService` with scoped lifetime

**Testing:**
- Uses xUnit with `Microsoft.AspNetCore.Mvc.Testing` for integration tests
- In-memory database via `Microsoft.EntityFrameworkCore.InMemory`
- Tests located in `PhotoAlbum.Tests/Unit/Services/`

## Key Design Patterns

- **Service layer abstraction:** `IPhotoService` interface allows storage implementation swap (e.g., local → Azure Blob)
- **Transactional consistency:** File deletion on DB save failure ensures no orphaned files
- **Configuration-driven:** File size limits and allowed types in `appsettings.json`
