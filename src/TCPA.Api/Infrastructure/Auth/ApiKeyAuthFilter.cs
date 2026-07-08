// TCPA Regulatory Compliance API
// Component: API Key Authentication Filter (ADR-006)
// Source: EPIC-001 (STORY-002, STORY-003) | SPEC-001, NFS-007
// Generated: 2026-06-26

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TCPA.Api.Infrastructure.Auth;

/// <summary>
/// Action filter that enforces API key authentication for upstream SCG application endpoints.
/// The caller must supply a valid <c>X-API-Key</c> header that matches the configured key
/// (ADR-006, SPEC-001).
///
/// <para>
/// The expected API key is read at request time from <c>IConfiguration["Auth:ApiKey"]</c>
/// so that key rotation takes effect without a redeployment.
/// </para>
///
/// <para>
/// Security note: comparison uses <see cref="System.Security.Cryptography.CryptographicOperations.FixedTimeEquals"/>
/// to prevent timing oracle attacks on the key value.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiKeyAuthFilterAttribute : Attribute, IAsyncActionFilter
{
    /// <summary>The HTTP header name carrying the API key.</summary>
    public const string ApiKeyHeaderName = "X-API-Key";

    /// <summary>The IConfiguration key path for the expected API key value.</summary>
    private const string ApiKeyConfigPath = "Auth:ApiKey";

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        IConfiguration configuration = context.HttpContext.RequestServices
            .GetRequiredService<IConfiguration>();
        ILogger<ApiKeyAuthFilterAttribute> logger = context.HttpContext.RequestServices
            .GetRequiredService<ILogger<ApiKeyAuthFilterAttribute>>();

        string? expectedKey = configuration[ApiKeyConfigPath];

        if (string.IsNullOrEmpty(expectedKey))
        {
            // Fail closed: if the API key is not configured, deny all requests.
            logger.LogCritical(
                "API key authentication misconfiguration: '{ConfigPath}' is not set. " +
                "All requests to this endpoint will be rejected until the key is configured.",
                ApiKeyConfigPath);

            context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var suppliedKeyHeader)
            || string.IsNullOrEmpty(suppliedKeyHeader))
        {
            logger.LogWarning(
                "API key authentication failed: missing {HeaderName} header. " +
                "Path: {Path}.",
                ApiKeyHeaderName,
                context.HttpContext.Request.Path);

            context.Result = new UnauthorizedResult();
            return;
        }

        string suppliedKey = suppliedKeyHeader.ToString();

        // Constant-time comparison to prevent timing oracle attacks on the key.
        byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes(expectedKey);
        byte[] suppliedBytes = System.Text.Encoding.UTF8.GetBytes(suppliedKey);

        bool keyMatches = expectedBytes.Length == suppliedBytes.Length
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                expectedBytes, suppliedBytes);

        if (!keyMatches)
        {
            logger.LogWarning(
                "API key authentication failed: invalid key supplied. Path: {Path}.",
                context.HttpContext.Request.Path);

            context.Result = new UnauthorizedResult();
            return;
        }

        await next();
    }
}
