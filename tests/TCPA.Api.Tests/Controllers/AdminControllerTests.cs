using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TCPA.Api.Controllers;
using TCPA.Api.Models;
using TCPA.Core.Services;
using Xunit;

namespace TCPA.Api.Tests.Controllers;

public class AdminControllerTests
{
    private readonly IReOptInService _reOptInService = Substitute.For<IReOptInService>();
    private readonly IPhoneNumberHasher _hasher = Substitute.For<IPhoneNumberHasher>();
    private readonly ILogger<AdminController> _logger = Substitute.For<ILogger<AdminController>>();

    private AdminController BuildSut() => new(_reOptInService, _hasher, _logger);

    [Fact]
    public async Task ReOptIn_ValidRequest_Returns200()
    {
        var effectiveAt = DateTime.UtcNow;
        _reOptInService.ExecuteAsync("+14045551234", "agent-jsmith", "Customer called to reverse STOP.", default)
            .Returns(new ReOptInResult(42L, effectiveAt));

        var result = await BuildSut().ReOptIn(new ReOptInRequest
        {
            PhoneNumber = "+14045551234",
            Reason = "Customer called to reverse STOP.",
            AgentId = "agent-jsmith"
        }, default);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ReOptInResponse>().Subject;
        response.ReOptInId.Should().Be(42L);
        response.Status.Should().Be("opted-in");
        response.PhoneNumber.Should().Be("+14045551234");
    }

    [Fact]
    public async Task ReOptIn_ServiceThrows_Returns500()
    {
        _reOptInService.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), default)
            .ThrowsAsync(new Exception("DB transaction failure"));

        var result = await BuildSut().ReOptIn(new ReOptInRequest
        {
            PhoneNumber = "+14045551234",
            Reason = "Reason",
            AgentId = "agent-1"
        }, default);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }
}
