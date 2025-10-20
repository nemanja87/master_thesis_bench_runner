using Microsoft.AspNetCore.Mvc;
using Shared.Security;

namespace AuthServer.Controllers;

[ApiController]
[Route("healthz")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            profile = SecurityProfileDefaults.CurrentProfile.ToString()
        });
    }
}
