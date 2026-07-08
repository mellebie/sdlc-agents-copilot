// TCPA Regulatory Compliance API
// Component: Correlation ID Accessor — Scoped Implementation
// Source: EPIC-007 (STORY-019) | TASK-053
// Generated: 2026-06-26

namespace TCPA.Api.Services.AuditLog;

/// <summary>
/// Scoped implementation of <see cref="ICorrelationIdAccessor"/>.
/// Each HTTP request receives its own instance; the middleware calls
/// <see cref="SetCorrelationId"/> once at the start of the pipeline before
/// any downstream service reads <see cref="CorrelationId"/>.
///
/// <para>
/// For background job contexts (e.g., Azure Functions) that do not have an HTTP
/// request scope, a fallback UUID is generated at construction time.
/// </para>
/// </summary>
public sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    private string _correlationId = Guid.NewGuid().ToString("D");

    /// <inheritdoc />
    public string CorrelationId => _correlationId;

    /// <summary>
    /// Sets the correlation ID for the current request. Called exclusively by
    /// <see cref="CorrelationIdMiddleware"/> at the start of the request pipeline.
    /// Calling this method after downstream processing has begun is a programming error.
    /// </summary>
    /// <param name="correlationId">
    /// The correlation ID extracted from the request header or freshly generated.
    /// Must not be null or whitespace.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="correlationId"/> is null or whitespace.
    /// </exception>
    public void SetCorrelationId(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID must not be null or whitespace.", nameof(correlationId));
        }

        _correlationId = correlationId;
    }
}
