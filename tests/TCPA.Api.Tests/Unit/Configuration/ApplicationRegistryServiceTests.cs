// tests/TCPA.Api.Tests/Unit/Configuration/ApplicationRegistryServiceTests.cs
// Tests for ApplicationRegistryService — application registry with in-memory caching
// Source: TASK-003, TASK-004, TASK-050 | SPEC-014 | STORY-002, STORY-003

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TCPA.Api.Domain;
using TCPA.Api.Infrastructure.Configuration;
using TCPA.Api.Infrastructure.Data;
using Xunit;

namespace TCPA.Api.Tests.Unit.Configuration;

/// <summary>
/// Tests for <see cref="ApplicationRegistryService"/>.
/// Verifies: known/unknown/inactive account lookups, cache hit prevents second DB call,
/// GetAllActiveAsync only returns IsActive=true, startup validation behaviors.
/// </summary>
public sealed class ApplicationRegistryServiceTests : IDisposable
{
    private readonly TcpaDbContext _dbContext;
    private readonly IMemoryCache _memoryCache;
    private readonly Mock<ILogger<ApplicationRegistryService>> _loggerMock;
    private readonly IOptions<ApplicationRegistryOptions> _options;

    public ApplicationRegistryServiceTests()
    {
        DbContextOptions<TcpaDbContext> dbOptions = new DbContextOptionsBuilder<TcpaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TcpaDbContext(dbOptions);
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<ApplicationRegistryService>>();
        _options = Options.Create(new ApplicationRegistryOptions { CacheTtlMinutes = 5 });
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _memoryCache.Dispose();
    }

    private ApplicationRegistryService BuildSut() =>
        new ApplicationRegistryService(_dbContext, _memoryCache, _loggerMock.Object, _options);

    private async Task SeedRegistrationAsync(
        string accountNumber,
        string appName,
        bool isActive = true)
    {
        _dbContext.ApplicationRegistrations.Add(new ApplicationRegistration
        {
            Id = Guid.NewGuid(),
            CoolTextAccountNumber = accountNumber,
            ApplicationName = appName,
            CallbackUrl = "https://callback.example.com/sms",
            IsActive = isActive,
            OnboardedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await _dbContext.SaveChangesAsync();
    }

    // -----------------------------------------------------------------------
    // GetByAccountNumberAsync — known active account returns entry
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnEntry_When_AccountNumberIsRegisteredAndActive()
    {
        // Arrange
        const string accountNumber = "ACCT-001";
        await SeedRegistrationAsync(accountNumber, "TestApp");

        ApplicationRegistryService sut = BuildSut();

        // Act
        ApplicationRegistryEntry? result = await sut.GetByAccountNumberAsync(accountNumber);

        // Assert
        result.Should().NotBeNull();
        result!.CoolTextAccountNumber.Should().Be(accountNumber);
        result.ApplicationName.Should().Be("TestApp");
        result.IsActive.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // GetByAccountNumberAsync — unknown account returns null
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnNull_When_AccountNumberIsNotRegistered()
    {
        // Arrange — no entries seeded
        ApplicationRegistryService sut = BuildSut();

        // Act
        ApplicationRegistryEntry? result = await sut.GetByAccountNumberAsync("UNKNOWN-ACCT");

        // Assert
        result.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // GetByAccountNumberAsync — inactive account returns null (BR-063)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnNull_When_AccountNumberExistsButIsInactive()
    {
        // Arrange
        const string accountNumber = "ACCT-INACTIVE";
        await SeedRegistrationAsync(accountNumber, "InactiveApp", isActive: false);

        ApplicationRegistryService sut = BuildSut();

        // Act
        ApplicationRegistryEntry? result = await sut.GetByAccountNumberAsync(accountNumber);

        // Assert — inactive treated as unregistered (BR-063)
        result.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // GetByAccountNumberAsync — null/empty account number returns null (no DB call)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Should_ReturnNull_When_AccountNumberIsNullOrWhiteSpace(string? accountNumber)
    {
        // Arrange
        ApplicationRegistryService sut = BuildSut();

        // Act
        ApplicationRegistryEntry? result = await sut.GetByAccountNumberAsync(accountNumber!);

        // Assert
        result.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // GetByAccountNumberAsync — cache hit: DB not queried twice for same key
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_NotQueryDatabase_OnSecondLookup_When_CacheIsWarm()
    {
        // Arrange — seed the DB and prime the cache via first call
        const string accountNumber = "ACCT-CACHED";
        await SeedRegistrationAsync(accountNumber, "CachedApp");

        ApplicationRegistryService sut = BuildSut();

        ApplicationRegistryEntry? firstResult = await sut.GetByAccountNumberAsync(accountNumber);
        firstResult.Should().NotBeNull();

        // Remove the DB record to prove the second lookup comes from cache
        ApplicationRegistration? dbRecord = await _dbContext.ApplicationRegistrations
            .FirstOrDefaultAsync(r => r.CoolTextAccountNumber == accountNumber);
        _dbContext.ApplicationRegistrations.Remove(dbRecord!);
        await _dbContext.SaveChangesAsync();

        // Act — second lookup: DB record is gone, but cache should still serve it
        ApplicationRegistryEntry? secondResult = await sut.GetByAccountNumberAsync(accountNumber);

        // Assert — cache hit returns same entry even though DB record is removed
        secondResult.Should().NotBeNull(
            because: "the result should be served from cache on second call");
        secondResult!.CoolTextAccountNumber.Should().Be(accountNumber);
    }

    // -----------------------------------------------------------------------
    // GetAllActiveAsync — only returns IsActive=true entries
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnOnlyActiveEntries_When_GetAllActiveAsyncCalled()
    {
        // Arrange — seed one active and one inactive
        await SeedRegistrationAsync("ACCT-ACTIVE", "ActiveApp", isActive: true);
        await SeedRegistrationAsync("ACCT-INACTIVE", "InactiveApp", isActive: false);

        ApplicationRegistryService sut = BuildSut();

        // Act
        IReadOnlyList<ApplicationRegistryEntry> results = await sut.GetAllActiveAsync();

        // Assert
        results.Should().HaveCount(1);
        results.Should().AllSatisfy(e => e.IsActive.Should().BeTrue());
        results.Should().NotContain(e => e.CoolTextAccountNumber == "ACCT-INACTIVE");
    }

    [Fact]
    public async Task Should_ReturnEmptyList_When_NoActiveEntriesExist()
    {
        // Arrange — seed only inactive
        await SeedRegistrationAsync("ACCT-INACTIVE", "InactiveApp", isActive: false);

        ApplicationRegistryService sut = BuildSut();

        // Act
        IReadOnlyList<ApplicationRegistryEntry> results = await sut.GetAllActiveAsync();

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_ReturnAllActiveEntries_When_MultipleActiveEntriesExist()
    {
        // Arrange
        await SeedRegistrationAsync("ACCT-001", "App1", isActive: true);
        await SeedRegistrationAsync("ACCT-002", "App2", isActive: true);
        await SeedRegistrationAsync("ACCT-003", "App3", isActive: true);

        ApplicationRegistryService sut = BuildSut();

        // Act
        IReadOnlyList<ApplicationRegistryEntry> results = await sut.GetAllActiveAsync();

        // Assert
        results.Should().HaveCount(3);
    }

    // -----------------------------------------------------------------------
    // GetAllActiveAsync — second call is served from cache
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ServeCachedAllActiveList_When_CalledTwice()
    {
        // Arrange
        await SeedRegistrationAsync("ACCT-BULK", "BulkApp", isActive: true);
        ApplicationRegistryService sut = BuildSut();

        IReadOnlyList<ApplicationRegistryEntry> firstResult = await sut.GetAllActiveAsync();

        // Remove the DB record to confirm second call uses cache
        ApplicationRegistration? dbRecord = await _dbContext.ApplicationRegistrations
            .FirstOrDefaultAsync(r => r.CoolTextAccountNumber == "ACCT-BULK");
        _dbContext.ApplicationRegistrations.Remove(dbRecord!);
        await _dbContext.SaveChangesAsync();

        // Act
        IReadOnlyList<ApplicationRegistryEntry> secondResult = await sut.GetAllActiveAsync();

        // Assert — same count from cache, despite DB record being gone
        secondResult.Should().HaveCount(firstResult.Count,
            because: "second GetAllActiveAsync call must be served from cache");
    }

    // -----------------------------------------------------------------------
    // GetAllActiveAsync — populates individual account keys in cache
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_AllowByAccountNumberLookup_After_GetAllActiveAsync_PrimesCache()
    {
        // Arrange
        const string accountNumber = "ACCT-PRIME";
        await SeedRegistrationAsync(accountNumber, "PrimedApp", isActive: true);
        ApplicationRegistryService sut = BuildSut();

        // Prime individual keys via GetAllActiveAsync
        await sut.GetAllActiveAsync();

        // Remove from DB — if cache was primed, per-key lookup still works
        ApplicationRegistration? dbRecord = await _dbContext.ApplicationRegistrations
            .FirstOrDefaultAsync(r => r.CoolTextAccountNumber == accountNumber);
        _dbContext.ApplicationRegistrations.Remove(dbRecord!);
        await _dbContext.SaveChangesAsync();

        // Act — GetByAccountNumberAsync should be served from the per-key cache
        ApplicationRegistryEntry? result = await sut.GetByAccountNumberAsync(accountNumber);

        // Assert
        result.Should().NotBeNull(
            because: "GetAllActiveAsync bulk load should populate per-key cache entries");
    }

    // -----------------------------------------------------------------------
    // StartupService validation: throws on invalid HTTPS callback URL
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ThrowInvalidOperationException_When_CallbackUrlIsNotHttps()
    {
        // Arrange — insert a record with http:// callback
        _dbContext.ApplicationRegistrations.Add(new ApplicationRegistration
        {
            Id = Guid.NewGuid(),
            CoolTextAccountNumber = "ACCT-HTTP",
            ApplicationName = "InsecureApp",
            CallbackUrl = "http://insecure.example.com/sms",  // NOT https
            IsActive = true,
            OnboardedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        var startupLogger = new Mock<ILogger<ApplicationRegistryStartupService>>();
        ApplicationRegistryService registrySvc = BuildSut();
        var startupSvc = new ApplicationRegistryStartupService(registrySvc, startupLogger.Object, _options);

        // Act
        Func<Task> act = async () => await startupSvc.StartAsync(CancellationToken.None);

        // Assert — startup must abort (TASK-004)
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*non-HTTPS*");
    }

    // -----------------------------------------------------------------------
    // StartupService validation: logs warning for missing required app (not throws)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_LogWarning_When_RequiredApplicationIsMissingFromRegistry()
    {
        // Arrange — no registrations at all; the 5 required apps are missing
        var startupLogger = new Mock<ILogger<ApplicationRegistryStartupService>>();
        ApplicationRegistryService registrySvc = BuildSut();
        var startupSvc = new ApplicationRegistryStartupService(registrySvc, startupLogger.Object, _options);

        // Act — should NOT throw for missing apps, only log warnings (TASK-050)
        Func<Task> act = async () => await startupSvc.StartAsync(CancellationToken.None);

        // Assert — no exception for missing applications
        await act.Should().NotThrowAsync(
            because: "missing required applications should warn but not abort startup (TASK-050)");

        // Verify at least one warning was logged for missing apps
        startupLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("not present in the active registry")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
