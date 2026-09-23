# Configuration and secrets modernization

- Added managed-identity Azure App Configuration loading using `AZURE_APP_CONFIGURATION_ENDPOINT`.
- Added optional Azure Key Vault configuration loading using `AZURE_KEY_VAULT_URI`; Key Vault values override environment/file providers.
- Removed the LocalDB connection string from source-controlled configuration; non-secret settings remain as cutover defaults until each environment is seeded into Azure App Configuration.
- Removed local fallback values for admin credentials and upload-path configuration so missing managed configuration fails closed.
- Added `.azure/configuration-migration.json` containing the non-secret app settings to seed into Azure App Configuration. Connection strings and passwords are intentionally excluded.
