using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PhotoAlbum.Tests.PostMigration.Infrastructure;

/// <summary>
/// EF Core interceptor that fails query compilation unconditionally, used to simulate
/// "the photo metadata store is unavailable" (TC-WEB-003) purely at the mocked EF Core
/// InMemory-provider boundary — no real Azure SQL Database is involved. This is a
/// provider-agnostic interception point (<see cref="IQueryExpressionInterceptor"/>), so it
/// works identically against the InMemory provider used by the post-migration test host.
/// </summary>
public sealed class ThrowingQueryExpressionInterceptor : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        => throw new InvalidOperationException("Simulated photo metadata store outage (TC-WEB-003 test double).");
}
