using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TCPA.Api.Filters;
using TCPA.Api.Models;
using TCPA.Core.Services;

namespace TCPA.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
[ServiceFilter(typeof(AdminApiKeyAuthFilter))]
public class AdminController : ControllerBase
{
    private readonly IReOptInService _reOptInService;
    private readonly IPhoneNumberHasher _hasher;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IReOptInService reOptInService, IPhoneNumberHasher hasher, ILogger<AdminController> logger)
    {
        _reOptInService = reOptInService;
        _hasher = hasher;
        _logger = logger;
    }

    /// <summary>Help Desk agent re-opts-in a customer. Rate-limited to 10 req/min per API key.</summary>
    [HttpPost("reopt-in")]
    [EnableRateLimiting("AdminReOptIn")]
    [ProducesResponseType(typeof(ReOptInResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(429)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> ReOptIn([FromBody] ReOptInRequest request, CancellationToken ct)
    {
        ReOptInResult result;
        try
        {
            result = await _reOptInService.ExecuteAsync(request.PhoneNumber, request.AgentId, request.Reason, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReOptIn failed for {PhoneHash}", _hasher.Hash(request.PhoneNumber));
            return StatusCode(500, new { error = "Re-opt-in failed. The operation was rolled back." });
        }

        _logger.LogInformation("{EventType} phone {PhoneHash} by agent {AgentId}",
            LogEventTypes.AdminReOptIn, _hasher.Hash(request.PhoneNumber), request.AgentId);

        return Ok(new ReOptInResponse(result.ReOptInId, request.PhoneNumber, "opted-in", result.EffectiveAt));
    }
}
