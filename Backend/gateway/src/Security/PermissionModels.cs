using Microsoft.AspNetCore.Authorization;

namespace Gateway.Security;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        Permission = permission?.Trim() ?? string.Empty;
    }

    public string Permission { get; }
}

public sealed class AuthorizationMatrixOptions
{
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string[]> RolePermissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
