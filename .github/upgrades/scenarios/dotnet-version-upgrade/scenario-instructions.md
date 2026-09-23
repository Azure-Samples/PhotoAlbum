# Scenario Instructions

## User Preferences
### Execution Style
- Automatic flow mode enabled.
- Do not pause for review or approval; proceed end-to-end.
- Keep scope limited to runtime and package compatibility changes required for the .NET LTS upgrade.

### Technical Preferences
- Target framework: .NET 10 (current LTS)
- Preserve current application behavior and business logic.
- Avoid broad Azure replatforming changes outside compatibility updates required for the upgraded runtime.

## Decisions
- The upgrade will move the solution from net9.0 to net10.0.
- Microsoft.AspNetCore and EF Core packages will be aligned to the .NET 10 runtime version.
- Third-party libraries will only be updated when required for compatibility and stability; no unnecessary refactors will be introduced.
