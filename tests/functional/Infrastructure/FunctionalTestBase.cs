// Infrastructure/FunctionalTestBase.cs
// Source: Agent 09b (Drew) — Functional & E2E Tests
// Base class wiring xUnit fixture, typed HttpClient, DB seeding, and async polling for all journey/contract/smoke tests.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using TCPA.Core.Data;
using TCPA.Core.Models;
using Xunit;

namespace TCPA.Functional.Tests.Infrastructure;

/// <summary>
/// Base class for all TCPA functional tests.
/// <list type="bullet">
///   <item>HttpClient pre-configured with the standard API key header.</item>
///   <item>DB seeding helpers for <see cref="CoolTextAccount"/> and <see cref="OptOutStatus"/>.</item>
///   <item><see cref="WaitForConditionAsync"/> — async polling, no Thread.Sleep.</item>
/// </list>
/// </summary>
public abstract class FunctionalTestBase : IDisposable
{
    protected readonly TcpaTestFactory Factory;

    /// <summary>HttpClient with <c>X-Api-Key</c> pre-set to the valid test key.</summary>
    protected readonly HttpClient Client;

    protected FunctionalTestBase(TcpaTestFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
        Client.DefaultRequestHeaders.Add(TestApiKeys.HeaderName, TestApiKeys.ValidKey);
    }

    // ─── Client helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with NO default request headers.
    /// Use this to test missing/invalid authentication scenarios.
    /// </summary>
    protected HttpClient CreateUnauthenticatedClient() => Factory.CreateClient();

    // ─── Database seeding helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Adds a <see cref="CoolTextAccount"/> to the InMemory database and saves it.
    /// Default values match the happy-path fixtures used throughout these tests.
    /// </summary>
    protected async Task SeedCoolTextAccountAsync(
        string accountNumber = "CT-ACCT-001",
        string applicationId = "BizTalk",
        string applicationName = "Gas App",
        bool isActive = true)
    {
        await using var ctx = Factory.CreateTestDbContext();

        // Guard against duplicate seeds when test classes share a factory (IClassFixture)
        var exists = ctx.CoolTextAccounts.Any(a => a.AccountNumber == accountNumber);
        if (exists) return;

        ctx.CoolTextAccounts.Add(new CoolTextAccount
        {
            AccountNumber = accountNumber,
            ApplicationId = applicationId,
            ApplicationName = applicationName,
            CallbackUrl = "https://test.example.com/callback",
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Adds an <see cref="OptOutStatus"/> record to the InMemory database.
    /// Use unique phone numbers per test to prevent cross-test interference.
    /// </summary>
    protected async Task SeedOptOutStatusAsync(string phoneNumber, string status = "opted-out")
    {
        await using var ctx = Factory.CreateTestDbContext();

        var existing = ctx.OptOutStatuses.FirstOrDefault(s => s.PhoneNumber == phoneNumber);
        if (existing is not null)
        {
            existing.Status = status;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            ctx.OptOutStatuses.Add(new OptOutStatus
            {
                PhoneNumber = phoneNumber,
                Status = status,
                EffectiveAt = DateTime.UtcNow,
                AuditRecordId = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        await ctx.SaveChangesAsync();
    }

    /// <summary>Reads the current opt-out status for a phone number directly from the InMemory DB.</summary>
    protected async Task<string?> GetOptOutStatusAsync(string phoneNumber)
    {
        await using var ctx = Factory.CreateTestDbContext();
        return ctx.OptOutStatuses
            .FirstOrDefault(s => s.PhoneNumber == phoneNumber)?.Status;
    }

    // ─── Async polling helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Polls a condition predicate at regular intervals until it returns <c>true</c> or the
    /// timeout elapses. Use this for assertions that depend on async side-effects.
    /// Never uses <c>Thread.Sleep</c> or fixed delays.
    /// </summary>
    /// <param name="condition">Async predicate to check repeatedly.</param>
    /// <param name="timeout">Maximum wait time (default 10 s).</param>
    /// <param name="pollInterval">Time between checks (default 200 ms).</param>
    /// <param name="because">Assertion failure message when timeout is reached.</param>
    protected static async Task WaitForConditionAsync(
        Func<Task<bool>> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string because = "the condition should eventually be satisfied")
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(200);

        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(interval);
        }

        throw new Xunit.Sdk.XunitException($"Timeout reached waiting for: {because}");
    }

    // ─── IDisposable ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        Client.Dispose();
        GC.SuppressFinalize(this);
    }
}
