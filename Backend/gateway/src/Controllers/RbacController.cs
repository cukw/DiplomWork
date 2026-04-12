using System.Reflection;
using Gateway.Security;
using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Options;

namespace Gateway.Controllers;

[ApiController]
[Route("api/rbac")]
[Authorize]
public sealed class RbacController : ControllerBase
{
    private readonly RolePermissionStore _rolePermissionStore;
    private readonly IOptionsMonitor<AuthorizationMatrixOptions> _optionsMonitor;
    private readonly IAdminAuditLogger _auditLogger;

    public RbacController(
        RolePermissionStore rolePermissionStore,
        IOptionsMonitor<AuthorizationMatrixOptions> optionsMonitor,
        IAdminAuditLogger auditLogger)
    {
        _rolePermissionStore = rolePermissionStore;
        _optionsMonitor = optionsMonitor;
        _auditLogger = auditLogger;
    }

    [HttpGet("matrix")]
    public async Task<IActionResult> GetMatrix(CancellationToken cancellationToken)
    {
        var matrix = await _rolePermissionStore.GetMatrixAsync(cancellationToken);
        return Ok(new
        {
            enabled = _optionsMonitor.CurrentValue?.Enabled ?? true,
            rolePermissions = matrix.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
            availablePermissions = DiscoverAvailablePermissions()
        });
    }

    [HttpPut("matrix")]
    public async Task<IActionResult> SaveMatrix(
        [FromBody] UpdateRbacMatrixRequest request,
        CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        if (request?.RolePermissions is null)
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "rbac.matrix.update",
                actor,
                "rbac",
                "matrix",
                false,
                400,
                new { message = "rolePermissions payload is required" }), cancellationToken);
            return BadRequest(new { message = "rolePermissions payload is required" });
        }

        var saved = await _rolePermissionStore.SaveMatrixAsync(request.RolePermissions, cancellationToken);
        await _auditLogger.LogAsync(new AdminAuditEvent(
            "rbac.matrix.update",
            actor,
            "rbac",
            "matrix",
            true,
            200,
            new { roles = saved.Count, permissions = saved.Sum(pair => pair.Value.Length) }), cancellationToken);

        return Ok(new
        {
            enabled = _optionsMonitor.CurrentValue?.Enabled ?? true,
            rolePermissions = saved.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase)
        });
    }

    private static string[] DiscoverAvailablePermissions()
    {
        var actionPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wildcardPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(RbacController).Assembly;

        foreach (var controllerType in assembly
                     .GetTypes()
                     .Where(type =>
                         !type.IsAbstract
                         && typeof(ControllerBase).IsAssignableFrom(type)
                         && type.Name.EndsWith("Controller", StringComparison.Ordinal)))
        {
            var controllerName = controllerType.Name[..^"Controller".Length].ToLowerInvariant();
            wildcardPermissions.Add($"{controllerName}.*");

            foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<NonActionAttribute>() is not null)
                    continue;

                var hasHttpVerb = method
                    .GetCustomAttributes(inherit: true)
                    .Any(attribute => attribute is HttpMethodAttribute);
                if (!hasHttpVerb)
                    continue;

                actionPermissions.Add($"{controllerName}.{method.Name.ToLowerInvariant()}");
            }
        }

        return wildcardPermissions
            .Concat(actionPermissions)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    public sealed record UpdateRbacMatrixRequest(Dictionary<string, string[]> RolePermissions);
}
