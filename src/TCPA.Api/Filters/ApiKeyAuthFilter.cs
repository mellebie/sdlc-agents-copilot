using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TCPA.Api.Filters;

public class ApiKeyAuthFilter : IActionFilter
{
    private readonly HashSet<string> _validKeys;

    public ApiKeyAuthFilter(IConfiguration configuration)
    {
        var raw = configuration["ApiKeys:ValidKeys"] ?? string.Empty;
        _validKeys = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.Ordinal);
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var key = context.HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(key) || !_validKeys.Contains(key))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid or missing API key." });
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
