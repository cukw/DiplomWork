using Gateway.Data;
using Gateway.Models;
using Gateway.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gateway.Services;

public sealed class RolePermissionStore
{
    private static readonly StringComparer RoleComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer PermissionComparer = StringComparer.OrdinalIgnoreCase;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDbContextFactory<GatewayRuntimeDbContext> _dbFactory;
    private readonly IOptionsMonitor<AuthorizationMatrixOptions> _optionsMonitor;
    private readonly ILogger<RolePermissionStore> _logger;

    private Dictionary<string, string[]>? _cachedMatrix;
    private DateTime _cacheExpiresAtUtc = DateTime.MinValue;

    public RolePermissionStore(
        IDbContextFactory<GatewayRuntimeDbContext> dbFactory,
        IOptionsMonitor<AuthorizationMatrixOptions> optionsMonitor,
        ILogger<RolePermissionStore> logger)
    {
        _dbFactory = dbFactory;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, string[]>> GetMatrixAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedMatrix is not null && DateTime.UtcNow < _cacheExpiresAtUtc)
            return Clone(_cachedMatrix);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedMatrix is not null && DateTime.UtcNow < _cacheExpiresAtUtc)
                return Clone(_cachedMatrix);

            var loaded = await LoadFromDatabaseUnsafeAsync(cancellationToken);
            _cachedMatrix = loaded;
            _cacheExpiresAtUtc = DateTime.UtcNow.AddSeconds(30);
            return Clone(loaded);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, string[]>> SaveMatrixAsync(
        IDictionary<string, string[]> incomingMatrix,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incomingMatrix);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var sanitized = SanitizeMatrix(incomingMatrix);
            var now = DateTime.UtcNow;

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            await db.RolePermissions.ExecuteDeleteAsync(cancellationToken);
            if (sanitized.Count > 0)
            {
                var entities = new List<RolePermissionEntity>();
                foreach (var (role, permissions) in sanitized)
                {
                    foreach (var permission in permissions)
                    {
                        entities.Add(new RolePermissionEntity
                        {
                            RoleName = role,
                            Permission = permission,
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                    }
                }

                if (entities.Count > 0)
                    await db.RolePermissions.AddRangeAsync(entities, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            _cachedMatrix = sanitized;
            _cacheExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);

            _logger.LogInformation("Saved RBAC matrix to database. Roles={RoleCount}", sanitized.Count);
            return Clone(sanitized);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, string[]>> LoadFromDatabaseUnsafeAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var rows = await db.RolePermissions
            .AsNoTracking()
            .OrderBy(x => x.RoleName)
            .ThenBy(x => x.Permission)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            var fallback = SanitizeMatrix(_optionsMonitor.CurrentValue?.RolePermissions ?? new Dictionary<string, string[]>(RoleComparer));
            return fallback;
        }

        return rows
            .GroupBy(x => x.RoleName, RoleComparer)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => NormalizePermission(x.Permission))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(PermissionComparer)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray(),
                RoleComparer);
    }

    private static Dictionary<string, string[]> SanitizeMatrix(IDictionary<string, string[]> matrix)
    {
        var sanitized = new Dictionary<string, string[]>(RoleComparer);
        foreach (var (rawRole, rawPermissions) in matrix)
        {
            var role = NormalizeRole(rawRole);
            if (string.IsNullOrWhiteSpace(role))
                continue;

            var normalizedPermissions = (rawPermissions ?? [])
                .Select(NormalizePermission)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(PermissionComparer)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            if (normalizedPermissions.Length == 0)
                continue;

            sanitized[role] = normalizedPermissions;
        }

        return sanitized;
    }

    private static string NormalizeRole(string? role)
    {
        return string.IsNullOrWhiteSpace(role) ? string.Empty : role.Trim().ToLowerInvariant();
    }

    private static string NormalizePermission(string? permission)
    {
        return string.IsNullOrWhiteSpace(permission) ? string.Empty : permission.Trim().ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, string[]> Clone(IReadOnlyDictionary<string, string[]> source)
    {
        return source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            RoleComparer);
    }
}
