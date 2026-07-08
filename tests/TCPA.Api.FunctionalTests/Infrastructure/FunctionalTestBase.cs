// TCPA Functional Test Base Class
// Purpose: Shared infrastructure for all functional test classes — HTTP client helpers,
//          database seeding, HMAC signature computation, and async polling utilities.
// Source: Agent 09b | Tests/functional infrastructure

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TCPA.Api.Domain;
using TCPA.Api.Infrastructure.Data;

namespace TCPA.Api.FunctionalTests.Infrastructure;

/// <summary>
/// Base class for all TCPA functional test classes. Provides:
/// <list type="bullet">
///   <item>A pre-configured <see cref="HttpClient"/> with the factory's base address</item>
///   <item>Database seeding helpers that write directly to the InMemory EF Core context</item>
///   <item>Request builder helpers for API-key-authenticated and HMAC-signed requests</item>
///   <item>Async polling utility for fire-and-forget background operations</item>
/// </list>
/// </summary>
public abstract class FunctionalTestBase : IClassFixture<TcpaFunctionalTestFactory>
{
    protected readonly TcpaFunctionalTestFactory Factory;
    protected readonly HttpClient Client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    protected FunctionalTestBase(TcpaFunctionalTestFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    // -------------------------------------------------------------------------
    // Database seeding helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Seeds an active application registration into the InMemory database.
    /// Creates a new <see cref="IServiceScope"/> so EF Core tracked entities
    /// do not bleed between tests.
    /// </summary>
    protected async Task SeedApplicationRegistrationAsync(
        string coolTextAccountId,
        string appName,
        string callbackUrl = "https://callback.example.com/sms",
        bool isActive = true)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TcpaDbContext>();

        var existing = await db.ApplicationRegistrations
            .FirstOrDefaultAsync(r => r.CoolTextAccountNumber == coolTextAccountId);

        if (existing != null)
        {
            // Idempotent — do not create a duplicate
            return;
        }

        db.ApplicationRegistrations.Add(new ApplicationRegistration
        {
            Id = Guid.NewGuid(),
            CoolTextAccountNumber = coolTextAccountId,
            ApplicationName = appName,
            CallbackUrl = callbackUrl,
            IsActive = isActive,
            OnboardedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds an opt-out record into the InMemory database for the given cell number.
    /// </summary>
    protected async Task SeedOptOutRecordAsync(string cellNumber, OptOutStatus status)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TcpaDbContext>();

        var existing = await db.OptOutRecords
            .FirstOrDefaultAsync(r => r.CellPhoneNumber == cellNumber);

        if (existing != null)
        {
            existing.Status = status;
            existing.UpdatedAt = DateTime.UtcNow;
            if (status == OptOutStatus.OptOut)
            {
                existing.LastOptOutTimestamp = DateTime.UtcNow;
            }
            else
            {
                existing.LastOptInTimestamp = DateTime.UtcNow;
            }
        }
        else
        {
            db.OptOutRecords.Add(new CellNumberOptOutRecord
            {
                Id = Guid.NewGuid(),
                CellPhoneNumber = cellNumber,
                Status = status,
                LastOptOutTimestamp = status == OptOutStatus.OptOut ? DateTime.UtcNow : null,
                LastOptInTimestamp = status == OptOutStatus.OptIn ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Request builder helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds an <see cref="HttpRequestMessage"/> with the <c>X-API-Key</c> header
    /// required by <see cref="TCPA.Api.Infrastructure.Auth.ApiKeyAuthFilter"/>.
    /// </summary>
    protected HttpRequestMessage MakeApiKeyRequest(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TcpaTestConstants.ApiKeyHeaderName, TcpaTestConstants.ApiKey);

        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    /// <summary>
    /// Builds an <see cref="HttpRequestMessage"/> with an HMAC-SHA256 signature header
    /// computed using <see cref="TcpaTestConstants.WebhookSecret"/>.
    /// Signature format: <c>sha256={hex-encoded-hash}</c> (matches CoolTextWebhookValidator).
    /// </summary>
    protected HttpRequestMessage MakeHmacSignedRequest(HttpMethod method, string path, object body)
    {
        var jsonBody = JsonSerializer.Serialize(body);
        var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        var signature = ComputeHmacSignature(bodyBytes);

        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(TcpaTestConstants.SignatureHeaderName, signature);

        return request;
    }

    /// <summary>
    /// Builds an HMAC-SHA256 signature for the given body bytes.
    /// Uses HMAC key = <see cref="TcpaTestConstants.WebhookSecret"/>.
    /// Format: <c>sha256={lowercase-hex}</c>
    /// </summary>
    protected static string ComputeHmacSignature(byte[] bodyBytes)
    {
        var keyBytes = Encoding.UTF8.GetBytes(TcpaTestConstants.WebhookSecret);
        var hash = HMACSHA256.HashData(keyBytes, bodyBytes);
        return "sha256=" + Convert.ToHexString(hash).ToLower();
    }

    // -------------------------------------------------------------------------
    // Async polling utility
    // -------------------------------------------------------------------------

    /// <summary>
    /// Polls the given <paramref name="condition"/> function until it returns true,
    /// or throws <see cref="TimeoutException"/> if the <paramref name="timeoutMs"/>
    /// deadline is exceeded.
    /// <para>
    /// Use this for fire-and-forget background writes (e.g., inbound SMS opt-out
    /// processing happens AFTER the 200 response is returned). Never use fixed delays.
    /// </para>
    /// </summary>
    protected static async Task WaitForConditionAsync(
        Func<Task<bool>> condition,
        int timeoutMs = 5000,
        int pollIntervalMs = 100,
        string? timeoutMessage = null)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(pollIntervalMs);
        }

        throw new TimeoutException(
            timeoutMessage ?? $"Condition was not met within {timeoutMs}ms.");
    }

    /// <summary>
    /// Polls the InMemory database until an opt-out record exists for the given cell number,
    /// or throws <see cref="TimeoutException"/> after <paramref name="timeoutMs"/> ms.
    /// </summary>
    protected async Task WaitForOptOutRecordAsync(string cellNumber, int timeoutMs = 5000)
    {
        await WaitForConditionAsync(
            condition: async () =>
            {
                using var scope = Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TcpaDbContext>();
                return await db.OptOutRecords.AnyAsync(r => r.CellPhoneNumber == cellNumber);
            },
            timeoutMs: timeoutMs,
            timeoutMessage: $"Opt-out record for {cellNumber} was not written within {timeoutMs}ms.");
    }

    /// <summary>
    /// Reads the deserialized JSON response body as a <see cref="JsonElement"/> for
    /// flexible field-by-field assertions without requiring a concrete response DTO.
    /// </summary>
    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
    }
}
