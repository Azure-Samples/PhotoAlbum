# Upgrade Plan

## Objective
Upgrade the PhotoAlbum application to the current .NET LTS runtime and keep the solution buildable with compatible ASP.NET Core and EF Core packages.

## Strategy
1. Update the project target framework from net9.0 to net10.0.
2. Update Microsoft.AspNetCore and Entity Framework package references to .NET 10 compatible versions.
3. Update test project package versions to .NET 10 compatible versions.
4. Build and run unit tests to confirm no regressions.

## Scope constraints
- No business logic refactors.
- No broad Azure migration changes beyond runtime and compatibility updates.
- Local SQL configuration remains as-is unless required by the build/test pipeline.

## Execution checklist
- [ ] Set target frameworks to net10.0
- [ ] Refresh package versions for EF Core and ASP.NET Core dependencies
- [ ] Validate build output
- [ ] Run unit tests
- [ ] Confirm migration is complete
