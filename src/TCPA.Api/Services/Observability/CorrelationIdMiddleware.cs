// TCPA Regulatory Compliance API
// Component: Correlation ID Middleware
// Source: EPIC-007 (STORY-019) | TASK-053
// Generated: 2026-06-26

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TCPA.Api.Services.AuditLog;

/// <summary>
/// ASP.NET Core middleware that ensures every inbound HTTP request carries a
/// correlation ID. The correlation ID is propagated to all structured log events
/// within the request scope and returned in the response (TASK-053, SPEC-015).
///
/// <para>
/// Behaviour:
/// <list type="bullet">
///   <item>If the request carries an <c>X-Correlation-ID</c> header, that value is used.</item>
///   <item>If not, a new UUID is generated.</item>
///   <item>The correlation ID is added to the response as <c>X-Correlation-ID</c>.</item>
///   <item>The correlation ID is stored in the scoped <see cref="CorrelationIdAccessor"/>.</item>
///   <item>The correlation ID is pushed into the Serilog log context for all events in scope.</item>
/// </list>
/// </para>
///
/// <para>
/// Registration: This middleware must be registered before authentication and routing
/// so that all downstream log events include the correlation ID.
/// </para>
/// </summary>
public sealed class CorrelationIdMiddleware
{
    /// <summary>HTTP header name used to propagate the correlation ID.</summary>
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>Maximum accepted length for a caller-supplied correlation ID (SEC-002).</summary>
    private const int MaxCorrelationIdLength = 128;

    /// <summary>
    /// Allowed characters in a caller-supplied correlation ID: alphanumeric, hyphens,
    /// underscores. Rejects newlines and other characters that enable log injection (SEC-002).
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex SafeCorrelationIdPattern =
        new(@"^[A-Za-z0-9\-_]+$",
            System.Text.RegularExpressions.RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(50));

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CorrelationIdMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger for middleware-level events.</param>
    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes the HTTP request, assigns or propagates the correlation ID,
    /// and adds it to the response headers.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = Guid.NewGuid().ToString("D");
        if (context.Request.Headers.TryGetValue(HeaderName, out var headerValue)
            && !string.IsNullOrWhiteSpace(headerValue))
        {
            string candidate = headerValue.ToString();
            if (candidate.Length <= MaxCorrelationIdLength && SafeCorrelationIdPattern.IsMatch(candidate))
            {
                correlationId = candidate;
            }
            else
            {
                // Caller-supplied value failed validation — generate a fresh ID and log at Debug.
                // Do NOT log the rejected value (it may contain injection content).
                _logger.LogDebug(
                    "Rejected caller-supplied X-Correlation-ID (failed length or character validation). " +
                    "Generated replacement: {CorrelationId}.", correlationId);
            }
        }

        // Store on the scoped accessor so all services in this request can read it.
        CorrelationIdAccessor accessor = context.RequestServices
            .GetRequiredService<CorrelationIdAccessor>();
        accessor.SetCorrelationId(correlationId);

        // Echo the correlation ID back in the response.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Push into the log scope so all Serilog events within this request carry the ID.
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
        }))
        {
            await _next(context);
        }
    }
}
