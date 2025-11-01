using Microsoft.AspNetCore.Mvc;

namespace GameRagKit.Http;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { ok = true });
}
