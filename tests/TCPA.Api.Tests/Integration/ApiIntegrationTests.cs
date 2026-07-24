// ApiIntegrationTests.cs
// Source: Task 10 | Integration tests using WebApplicationFactory
// Tests the HTTP layer with all DB/Kafka dependencies mocked.
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using TCPA.Api.Messaging;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;
using TCPA.Core.Services;
using Xunit;

namespace TCPA.Api.Tests.Integration;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                // Replace Kafka publisher with a no-op mock that reports healthy
                var pub = services.SingleOrDefault(d => d.ServiceType == typeof(IMessagePublisher));
                if (pub is not null) services.Remove(pub);
                var mockPub = Substitute.For<IMessagePublisher>();
                mockPub.CheckHealthAsync(default).ReturnsForAnyArgs(true);
                services.AddSingleton(mockPub);

                // Replace CoolTextAccount repo — return an active account for any lookup
                var ct = services.SingleOrDefault(d => d.ServiceType == typeof(ICoolTextAccountRepository));
                if (ct is not null) services.Remove(ct);
                var mockCt = Substitute.For<ICoolTextAccountRepository>();
                mockCt.GetByAccountNumberAsync(Arg.Any<string>(), default)
                    .ReturnsForAnyArgs(new CoolTextAccount { AccountNumber = "CT-001", ApplicationId = "biztalk", IsActive = true });
                services.AddScoped(_ => mockCt);

                // Replace ProcessedMessage repo — no existing records (new messages pass idempotency)
                var pm = services.SingleOrDefault(d => d.ServiceType == typeof(IProcessedMessageRepository));
                if (pm is not null) services.Remove(pm);
                var mockPm = Substitute.For<IProcessedMessageRepository>();
                mockPm.FindAsync(Arg.Any<string>(), Arg.Any<string>(), default).ReturnsForAnyArgs((ProcessedMessage?)null);
                services.AddScoped(_ => mockPm);

                // Replace OptOutStatus repo — all phone numbers considered opted-in
                var st = services.SingleOrDefault(d => d.ServiceType == typeof(IOptOutStatusRepository));
                if (st is not null) services.Remove(st);
                var mockSt = Substitute.For<IOptOutStatusRepository>();
                mockSt.IsOptedOutAsync(Arg.Any<string>(), default).ReturnsForAnyArgs(false);
                services.AddScoped(_ => mockSt);

                // Replace ReOptInService — AdminController needs it; avoid real DB calls
                var rs = services.SingleOrDefault(d => d.ServiceType == typeof(IReOptInService));
                if (rs is not null) services.Remove(rs);
                var mockRs = Substitute.For<IReOptInService>();
                mockRs.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), default)
                    .ReturnsForAnyArgs(new ReOptInResult(1L, DateTime.UtcNow));
                services.AddScoped(_ => mockRs);
            }));
    }

    /// <summary>
    /// Creates an HttpClient pre-configured with the given API key header.
    /// Uses the test-environment placeholder key from appsettings.json unless overridden.
    /// The placeholder "REPLACE_IN_ENV" is the value in appsettings.json — production
    /// deployments override this via environment variable or secrets management.
    /// </summary>
    private HttpClient BuildClient(string apiKey = "REPLACE_IN_ENV")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return client;
    }

    // ------------------------------------------------------------------
    // Health endpoint — no API key required
    // ------------------------------------------------------------------

    [Fact]
    public async Task Health_NoApiKeyRequired_Returns200Or503()
    {
        var client = _factory.CreateClient();  // no API key header

        var response = await client.GetAsync("/api/v1/health");

        // 200 when DB reachable; 503 when not — both are valid in a CI environment with no SQL
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    // ------------------------------------------------------------------
    // Inbound webhook — auth + validation
    // ------------------------------------------------------------------

    [Fact]
    public async Task InboundWebhook_MissingApiKey_Returns401()
    {
        var client = _factory.CreateClient();  // no API key

        var response = await client.PostAsJsonAsync("/webhook/inbound", new
        {
            from = "+14045551234",
            to = "CT-001",
            body = "STOP",
            provider = "cooltext",
            messageId = "m-missing-key",
            timestamp = DateTimeOffset.UtcNow
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InboundWebhook_ValidRequest_Returns200()
    {
        var client = BuildClient();

        var response = await client.PostAsJsonAsync("/webhook/inbound", new
        {
            from = "+14045551234",
            to = "CT-001",
            body = "STOP",
            provider = "cooltext",
            messageId = $"m-{Guid.NewGuid()}",
            timestamp = DateTimeOffset.UtcNow
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InboundWebhook_InvalidE164_Returns400()
    {
        var client = BuildClient();

        var response = await client.PostAsJsonAsync("/webhook/inbound", new
        {
            from = "not-e164",   // fails E.164 regex on [From]
            to = "CT-001",
            body = "STOP",
            provider = "cooltext",
            messageId = "m-bad-phone",
            timestamp = DateTimeOffset.UtcNow
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------
    // Outbound messages — auth + happy path
    // ------------------------------------------------------------------

    [Fact]
    public async Task OutboundMessages_MissingApiKey_Returns401()
    {
        var client = _factory.CreateClient();  // no API key

        var response = await client.PostAsJsonAsync("/api/v1/messages/outbound", new
        {
            toNumber = "+14045551234",
            body = "Test",
            coolTextAccountNumber = "CT-001",
            applicationId = "biztalk"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OutboundMessages_OptedInNumber_Returns200Queued()
    {
        var client = BuildClient();

        var response = await client.PostAsJsonAsync("/api/v1/messages/outbound", new
        {
            toNumber = "+14045551234",
            body = "Your bill is due.",
            coolTextAccountNumber = "CT-001",
            applicationId = "biztalk"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------
    // Admin re-opt-in — auth required
    // ------------------------------------------------------------------

    [Fact]
    public async Task AdminReOptIn_MissingApiKey_Returns401()
    {
        var client = _factory.CreateClient();  // no API key

        var response = await client.PostAsJsonAsync("/api/v1/admin/reopt-in", new
        {
            phoneNumber = "+14045551234",
            reason = "Customer called.",
            agentId = "agent-1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
