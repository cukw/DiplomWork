using Gateway.Models;
using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

[ApiController]
[Route("api/app-settings")]
[Authorize]
public sealed class AppSettingsController : ControllerBase
{
    private readonly AppSettingsStore _store;

    public AppSettingsController(AppSettingsStore store)
    {
        _store = store;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var settings = await _store.GetAsync(cancellationToken);
        return Ok(settings);
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] AppSettingsDocument document, CancellationToken cancellationToken)
    {
        var saved = await _store.SaveAsync(document, cancellationToken);
        return Ok(saved);
    }
}
