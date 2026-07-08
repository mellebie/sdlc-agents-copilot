// TCPA Regulatory Compliance API
// Component: Correlation ID Accessor Interface
// Source: EPIC-007 (STORY-019) | SPEC-015 / TASK-053
// Generated: 2026-06-26

namespace TCPA.Api.Services.AuditLog;

/// <summary>
/// Provides access to the correlation ID associated with the current HTTP request.
/// The correlation ID is injected into every structured log event to allow
/// tracing of a single request across all service layers (TASK-053, NFS-008, SPEC-015).
///
/// <para>
/// Implementations must be registered as scoped services so that each HTTP request
/// receives its own correlation ID context.
/// </para>
/// </summary>
public interface ICorrelationIdAccessor
{
    /// <summary>
    /// Gets the correlation ID for the current request.
    /// Returns a UUID string generated at the start of the request pipeline.
    /// If called outside of an HTTP request context, returns a fallback UUID
    /// generated at accessor creation time (for background job contexts).
    /// </summary>
    string CorrelationId { get; }
}
