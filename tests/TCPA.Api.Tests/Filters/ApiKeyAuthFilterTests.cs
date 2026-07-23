using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using TCPA.Api.Filters;
using Xunit;

namespace TCPA.Api.Tests.Filters;

public class ApiKeyAuthFilterTests
{
    private static ApiKeyAuthFilter BuildFilter(string validKeys)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiKeys:ValidKeys"] = validKeys })
            .Build();
        return new ApiKeyAuthFilter(config);
    }

    private static ActionExecutingContext BuildContext(string? apiKey)
    {
        var httpContext = new DefaultHttpContext();
        if (apiKey is not null)
            httpContext.Request.Headers["X-Api-Key"] = apiKey;

        var actionContext = new ActionContext(httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new ActionDescriptor());
        return new ActionExecutingContext(actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());
    }

    [Fact]
    public void OnActionExecuting_ValidKey_DoesNotSetResult()
    {
        var filter = BuildFilter("valid-key-1,valid-key-2");
        var ctx = BuildContext("valid-key-1");

        filter.OnActionExecuting(ctx);

        ctx.Result.Should().BeNull();
    }

    [Fact]
    public void OnActionExecuting_InvalidKey_Returns401()
    {
        var filter = BuildFilter("valid-key-1");
        var ctx = BuildContext("wrong-key");

        filter.OnActionExecuting(ctx);

        var result = ctx.Result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public void OnActionExecuting_MissingHeader_Returns401()
    {
        var filter = BuildFilter("valid-key-1");
        var ctx = BuildContext(null);

        filter.OnActionExecuting(ctx);

        ctx.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public void OnActionExecuting_EmptyKey_Returns401()
    {
        var filter = BuildFilter("valid-key-1");
        var ctx = BuildContext("");

        filter.OnActionExecuting(ctx);

        ctx.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}
