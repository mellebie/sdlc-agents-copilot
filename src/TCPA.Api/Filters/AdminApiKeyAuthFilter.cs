using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TCPA.Api.Filters;

public class AdminApiKeyAuthFilter : IActionFilter
{
    private readonly HashSet<string> _adminKeys;

    public AdminApiKeyAuthFilter(IConfiguration configuration)
    {
        var raw = configuration["ApiKeys:AdminKeys"] ?? string.Empty;
        _adminKeys = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.Ordinal);
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var key = context.HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(key) || !_adminKeys.Contains(key))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid or missing admin API key." });
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
