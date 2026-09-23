using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PhotoAlbum.Tests.PostMigration.Infrastructure;

/// <summary>
/// EF Core interceptor that records the number of query compilations issued against the
/// intercepted <c>DbContext</c>, without altering behavior. Used to prove negative-verification
/// bullets such as "No query is issued against the Photos table" (TC-WEB-017, TC-WEB-024,
/// TC-WEB-025) by asserting <see cref="QueryCount"/> stays at 0 after the request completes —
/// a genuine observation rather than an existence/no-throw-only check.
/// </summary>
public sealed class QueryCountingInterceptor : IQueryExpressionInterceptor
{
    private int _queryCount;

    public int QueryCount => _queryCount;

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        Interlocked.Increment(ref _queryCount);
        return queryExpression;
    }
}
