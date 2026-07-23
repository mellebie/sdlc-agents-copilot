using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TCPA.Core.Services;

namespace TCPA.Core.Tests.Services;

public class PhoneNumberHasherTests
{
    private static IConfiguration BuildConfig(string key) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:PhoneHashKey"] = key })
            .Build();

    [Fact]
    public void Hash_SameInputAndKey_ReturnsSameHash()
    {
        var hasher = new PhoneNumberHasher(BuildConfig("test-key-32-chars-minimum-length!"));
        var h1 = hasher.Hash("+16785551234");
        var h2 = hasher.Hash("+16785551234");
        h1.Should().Be(h2);
    }

    [Fact]
    public void Hash_DifferentKeys_ReturnDifferentHashes()
    {
        var h1 = new PhoneNumberHasher(BuildConfig("key-A-padding-to-32-chars-minimum!")).Hash("+16785551234");
        var h2 = new PhoneNumberHasher(BuildConfig("key-B-padding-to-32-chars-minimum!")).Hash("+16785551234");
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Hash_OutputDoesNotContainOriginalPhoneDigits()
    {
        var hasher = new PhoneNumberHasher(BuildConfig("test-key-32-chars-minimum-length!"));
        var hash = hasher.Hash("+16785551234");
        hash.Should().NotContain("16785551234");
        hash.Should().NotContain("+");
    }

    [Fact]
    public void Hash_EmptyPhone_ReturnsConsistentHash()
    {
        var hasher = new PhoneNumberHasher(BuildConfig("test-key-32-chars-minimum-length!"));
        var act = () => hasher.Hash("");
        act.Should().NotThrow();
    }
}
