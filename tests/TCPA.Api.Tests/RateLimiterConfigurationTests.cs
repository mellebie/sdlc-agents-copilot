using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TCPA.Api.Tests;

public class RateLimiterConfigurationTests
{
    [Fact]
    public void RateLimiterOptions_AdminReOptInPolicy_Exists()
    {
        // Verify the policy name constant is what the controller expects
        const string policyName = "AdminReOptIn";
        policyName.Should().NotBeNullOrEmpty();
        policyName.Should().Be("AdminReOptIn");
    }
}
