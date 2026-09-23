# PhotoAlbum

A simple photo storage and gallery application built with ASP.NET Core Razor Pages, Entity Framework Core, and Bootstrap.

## Features

- 📸 **Photo Upload**: Drag-and-drop or click to upload multiple photos
- 🖼️ **Gallery View**: Responsive grid layout for browsing uploaded photos
- � **Photo Detail View**: Click any photo to view full-size with metadata and navigation
- �📊 **Metadata Display**: View file size, dimensions, aspect ratio, and upload timestamp
- ⬅️➡️ **Photo Navigation**: Previous/Next buttons to browse through photos
- ✅ **Validation**: File type and size validation (JPEG, PNG, GIF, WebP; max 10MB)
- 🗄️ **Database Storage**: Photo metadata stored in Azure SQL Database
- 🗑️ **Delete Photos**: Remove photos from both gallery and detail views
- 🎨 **Modern UI**: Clean, responsive design with Bootstrap 5

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- Azure SQL Database access with managed identity configuration
- A modern web browser

## Getting Started

### 1. Clone the Repository

```bash
git clone <repository-url>
cd PhotoAlbum
```

### 2. Restore Dependencies

```bash
dotnet restore
```

### 3. Apply Database Migrations

The application uses Entity Framework Core with Azure SQL Database. Migrations are applied automatically on first run, or you can apply them manually:

```bash
dotnet ef database update --project PhotoAlbum
```

### 4. Run the Application

```bash
cd PhotoAlbum
dotnet run
```

The application will be available at `http://localhost:5000` (or `https://localhost:5001` for HTTPS).

## Project Structure

```
PhotoAlbum/
├── PhotoAlbum/                 # Main web application
│   ├── Data/                   # Database context and migrations
│   ├── Models/                 # Domain models (Photo, UploadResult)
│   ├── Pages/                  # Razor Pages (Index, Error, etc.)
│   ├── Services/               # Business logic (PhotoService)
│   ├── wwwroot/                # Static files (CSS, JS, uploaded photos)
│   └── Program.cs              # Application startup
├── PhotoAlbum.Tests/           # Test project
│   ├── Unit/                   # Unit tests
│   └── Integration/            # Integration tests
└── PhotoAlbum.sln              # Solution file
```

## Configuration

Configuration is stored in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=tcp:<sql-server-name>.database.windows.net,1433;Initial Catalog=<database-name>;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
  },
  "Storage": {
    "AccountName": "your-storage-account-name",
    "ContainerName": "photos",
    "ServiceUri": "https://your-storage-account-name.blob.core.windows.net"
  },
  "FileUpload": {
    "MaxFileSizeBytes": 10485760,
    "AllowedMimeTypes": ["image/jpeg", "image/png", "image/gif", "image/webp"]
  }
}
```

### Configuration Options

- **MaxFileSizeBytes**: Maximum file size in bytes (default: 10MB)
- **AllowedMimeTypes**: Array of accepted MIME types
- **Storage:ContainerName**: Blob container hosting uploaded photos
- **Storage:ServiceUri**: Azure Blob Storage endpoint used by `DefaultAzureCredential`

## Running Tests

The project includes both unit tests and integration tests.

### Run All Tests

```bash
dotnet test
```

### Run Unit Tests Only

```bash
dotnet test --filter Category=Unit
```

### Run Integration Tests Only

```bash
dotnet test --filter Category=Integration
```

## Development

### Technology Stack

- **Framework**: ASP.NET Core 9.0 (Razor Pages)
- **Database**: Azure SQL Database with Entity Framework Core 9.0 using managed identity
- **Testing**: xUnit with WebApplicationFactory
- **Image Processing**: SixLabors.ImageSharp 3.1.11
- **Frontend**: Bootstrap 5.3.0, Vanilla JavaScript

### Architecture

The application uses a clean architecture with separation of concerns:

- **Presentation Layer**: Razor Pages (Index, Detail) and JavaScript
- **Service Layer**: PhotoService for business logic
- **Data Layer**: Entity Framework Core with PhotoAlbumContext
- **Storage Abstraction**: Indirect image serving via `/photo/{id}` endpoint
  - Frontend uses photo IDs, not file paths
  - Enables future migration to cloud storage (Azure Blob, AWS S3, etc.)
  - No frontend changes needed when changing storage backend

See [FEATURE_INDIRECT_IMAGE_SERVING.md](FEATURE_INDIRECT_IMAGE_SERVING.md) for details on the image serving architecture.

### Code Quality

The project follows standard C# coding conventions and includes:

- XML documentation on all public APIs
- Comprehensive unit test coverage
- Integration tests for page handlers
- Consistent formatting (enforced via `dotnet format`)

### Building for Release

```bash
dotnet build --configuration Release
```

### Publishing

```bash
dotnet publish --configuration Release --output ./publish
```

## Usage

1. **Upload Photos**:
   - Drag and drop one or more photos onto the upload zone
   - Or click "Browse" to select files from your computer
   - Supported formats: JPEG, PNG, GIF, WebP (max 10MB each)

2. **View Gallery**:
   - Uploaded photos appear in a responsive grid
   - Each card shows the photo thumbnail, filename, dimensions, file size, and upload date
   - Click on any photo thumbnail or filename to view it full-size

3. **View Photo Details**:
   - Click any photo to open the detail view
   - See full-size image with comprehensive metadata
   - Use Previous/Next buttons to navigate between photos
   - Click "Back to Gallery" to return to the main page

4. **Delete Photos**:
   - Click the "Delete" button on the detail page to remove a photo
   - Confirmation dialog will appear before deletion
   - Photo is removed from both the database and file system## Troubleshooting

### Database Connection Issues

If you encounter database connection errors:

1. Verify the Azure SQL server and database are reachable from the app environment.
2. Ensure the application managed identity has access to the database.
3. Update the `DefaultConnection` connection string or environment variables to the correct Azure SQL endpoint.

### Storage Access

Ensure the application identity has `Storage Blob Data Contributor` access to the configured Azure Blob container. The container is created automatically on first upload when the configured account and identity are valid.

## License

This project is provided as-is for educational and demonstration purposes.

## Contributing

Contributions are welcome! Please ensure:

- All tests pass (`dotnet test`)
- Code is properly formatted (`dotnet format`)
- XML documentation is added for public APIs
- New features include appropriate tests

## Support

For issues or questions, please open an issue in the repository.
