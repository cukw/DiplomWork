using System.Security.Claims;
using Gateway.Services;
using Microsoft.Extensions.Options;

namespace Gateway.Security;

public sealed class PermissionEvaluator
{
    private static readonly StringComparer PermissionComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> LocalAgentPermissions = new(PermissionComparer)
    {
        "user.enrollcomputer",
        "user.endcomputersession"
    };

    private readonly IOptionsMonitor<AuthorizationMatrixOptions> _optionsMonitor;
    private readonly RolePermissionStore _rolePermissionStore;
    private readonly ILogger<PermissionEvaluator> _logger;

    public PermissionEvaluator(
        IOptionsMonitor<AuthorizationMatrixOptions> optionsMonitor,
        RolePermissionStore rolePermissionStore,
        ILogger<PermissionEvaluator> logger)
    {
        _optionsMonitor = optionsMonitor;
        _rolePermissionStore = rolePermissionStore;
        _logger = logger;
    }

    public async Task<bool> HasPermissionAsync(
        ClaimsPrincipal principal,
        string permission,
        CancellationToken cancellationToken = default)
    {
        var required = NormalizePermission(permission);
        if (string.IsNullOrWhiteSpace(required))
            return true;

        var roles = principal.Claims
            .Where(c => c.Type == ClaimTypes.Role || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase))
            .SelectMany(c => SplitClaimValue(c.Value))
            .Select(r => r.Trim())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var isAdmin = roles.Any(role => string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase));
        if (!isAdmin && !LocalAgentPermissions.Contains(required))
        {
            _logger.LogWarning(
                "RBAC denied non-admin access to panel permission. Permission={Permission}, Roles={Roles}",
                required,
                roles.Length == 0 ? "-" : string.Join(",", roles));
            return false;
        }

        var options = _optionsMonitor.CurrentValue ?? new AuthorizationMatrixOptions();
        if (!options.Enabled)
            return true;

        var rolePermissions = await _rolePermissionStore.GetMatrixAsync(cancellationToken);
        if (rolePermissions.Count == 0)
        {
            rolePermissions = options.RolePermissions ?? new Dictionary<string, string[]>(PermissionComparer);
        }

        if (rolePermissions.Count == 0)
        {
            _logger.LogWarning(
                "RBAC matrix is empty while AuthorizationMatrix is enabled. Denying permission {Permission}.",
                required);
            return false;
        }

        var explicitPermissions = principal.Claims
            .Where(c => string.Equals(c.Type, "permission", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(c.Type, "permissions", StringComparison.OrdinalIgnoreCase))
            .SelectMany(c => SplitClaimValue(c.Value))
            .Select(NormalizePermission)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(PermissionComparer)
            .ToArray();

        if (explicitPermissions.Any(granted => IsPermissionMatch(granted, required)))
            return true;

        foreach (var role in roles)
        {
            if (!rolePermissions.TryGetValue(role, out var grants) || grants is null || grants.Length == 0)
                continue;

            foreach (var grant in grants)
            {
                if (IsPermissionMatch(NormalizePermission(grant), required))
                    return true;
            }
        }

        _logger.LogWarning(
            "RBAC denied action. Permission={Permission}, Roles={Roles}",
            required,
            roles.Length == 0 ? "-" : string.Join(",", roles));

        return false;
    }

    private static bool IsPermissionMatch(string grantedPermission, string requiredPermission)
    {
        if (string.IsNullOrWhiteSpace(grantedPermission))
            return false;
        if (PermissionComparer.Equals(grantedPermission, "*"))
            return true;
        if (PermissionComparer.Equals(grantedPermission, requiredPermission))
            return true;

        if (grantedPermission.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = grantedPermission[..^2];
            if (requiredPermission.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> SplitClaimValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizePermission(string? permission)
    {
        return string.IsNullOrWhiteSpace(permission)
            ? string.Empty
            : permission.Trim().ToLowerInvariant();
    }
}
