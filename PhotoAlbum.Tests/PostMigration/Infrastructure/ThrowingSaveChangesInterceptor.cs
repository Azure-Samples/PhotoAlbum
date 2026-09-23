using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PhotoAlbum.Tests.PostMigration.Infrastructure;

/// <summary>
/// EF Core interceptor that fails <c>SaveChangesAsync</c> unconditionally, used to simulate
/// a database write failure (TC-WEB-013: metadata save fails after storage write succeeds;
/// TC-WEB-022: delete's row-removal save fails) purely at the mocked EF Core InMemory-provider
/// boundary — no real Azure SQL Database is involved.
/// </summary>
public sealed class ThrowingSaveChangesInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated database save failure (test double).");
    }
}
