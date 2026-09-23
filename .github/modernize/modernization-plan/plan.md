# Modernization Plan: PhotoAlbum Azure Modernization

**Project**: PhotoAlbum

---

## Technical Framework

- **Language**: C# / .NET 9
- **Framework**: ASP.NET Core 9, Razor Pages
- **Build Tool**: MSBuild / dotnet
- **Database**: SQL Server LocalDB (current), Azure SQL Database (target)
- **Key Dependencies**: Entity Framework Core, SixLabors.ImageSharp, ASP.NET Core Authentication

---

## Overview

This migration modernizes PhotoAlbum from local infrastructure to Azure-native managed services. The application currently stores uploaded photos on local disk, reads configuration and database settings from appsettings and Web.config, and relies on server-local SQL Server LocalDB. The new architecture will:

- move photo storage to Azure Blob Storage and offload static content to Azure-hosted assets
- externalize configuration and secrets from local files to Azure App Configuration and Key Vault
- move the data layer to Azure SQL Database and use managed identity for secure access
- deploy the app to Azure Container Apps with secure, environment-based configuration

The migration follows a phased approach: baseline validation, runtime upgrade, Azure service migration, security hardening, and deployment verification.

---

## Migration Impact Summary

| Application | Original Service | New Azure Service | Authentication | Comments |
|-------------|------------------|-------------------|----------------|----------|
| PhotoAlbum | Local file uploads | Azure Blob Storage | Managed Identity | Replace local disk I/O with managed cloud storage |
| PhotoAlbum | LocalDB SQL Server | Azure SQL Database | Managed Identity | Remove local connection strings and SQL Server dependencies |
| PhotoAlbum | appsettings.json + Web.config | Azure App Configuration + Key Vault | Managed Identity | Externalize configuration and secrets |
| PhotoAlbum | Local ASP.NET host | Azure Container Apps | Managed Identity + Entra ID | Secure Azure deployment for the web app |

---

## Security Compliance

Scan all project dependencies and deployment configuration for known CVEs and security misconfigurations before release. Upgrade vulnerable packages to the minimum patched versions, document any major-version upgrade risk, and verify the app builds and the test suite remains green after remediation. This plan also removes local secret and connection-string patterns and relies on managed identity and environment-based config in Azure.
