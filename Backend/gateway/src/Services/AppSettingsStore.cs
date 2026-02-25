using System.Text.Json;
using Gateway.Models;

namespace Gateway.Services;

public sealed class AppSettingsStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public AppSettingsStore(IHostEnvironment environment)
    {
        var dataDir = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(dataDir);
        _settingsPath = Path.Combine(dataDir, "runtime-settings.json");
    }

    public async Task<AppSettingsDocument> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var doc = await LoadUnsafeAsync(cancellationToken);
            return Clone(doc);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppSettingsDocument> SaveAsync(AppSettingsDocument incoming, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var sanitized = Sanitize(incoming);
            sanitized.UpdatedAt = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(sanitized, _jsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json, cancellationToken);
            return Clone(sanitized);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AppSettingsDocument> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            var seeded = Sanitize(new AppSettingsDocument());
            seeded.UpdatedAt = DateTime.UtcNow;
            var seedJson = JsonSerializer.Serialize(seeded, _jsonOptions);
            await File.WriteAllTextAsync(_settingsPath, seedJson, cancellationToken);
            return seeded;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath, cancellationToken);
            var parsed = JsonSerializer.Deserialize<AppSettingsDocument>(json, _jsonOptions);
            return Sanitize(parsed ?? new AppSettingsDocument());
        }
        catch
        {
            // Corrupted file fallback: preserve service availability with defaults.
            var fallback = Sanitize(new AppSettingsDocument());
            fallback.UpdatedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(fallback, _jsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json, cancellationToken);
            return fallback;
        }
    }

    private static AppSettingsDocument Sanitize(AppSettingsDocument input)
    {
        var result = new AppSettingsDocument
        {
            GeneralSettings = input.GeneralSettings ?? new GeneralSettingsModel(),
            SecuritySettings = input.SecuritySettings ?? new SecuritySettingsModel(),
            NotificationSettings = input.NotificationSettings ?? new NotificationSettingsModel(),
            MonitoringSettings = input.MonitoringSettings ?? new MonitoringSettingsModel(),
            WhitelistEntries = NormalizeEntries(input.WhitelistEntries),
            BlacklistEntries = NormalizeEntries(input.BlacklistEntries),
            UpdatedAt = input.UpdatedAt == default ? DateTime.UtcNow : input.UpdatedAt.ToUniversalTime()
        };

        return result;
    }

    private static List<ApplicationListEntryModel> NormalizeEntries(List<ApplicationListEntryModel>? source)
    {
        var result = new List<ApplicationListEntryModel>();
        long nextId = 1;

        foreach (var entry in source ?? [])
        {
            var application = (entry.Application ?? string.Empty).Trim();
            var description = (entry.Description ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(application))
                continue;

            result.Add(new ApplicationListEntryModel
            {
                Id = entry.Id > 0 ? entry.Id : nextId,
                Application = application,
                Description = description
            });

            nextId = Math.Max(nextId + 1, (entry.Id > 0 ? entry.Id : nextId) + 1);
        }

        // Reassign duplicates/missing IDs for stability in the UI.
        var seen = new HashSet<long>();
        long fallbackId = 1;
        foreach (var entry in result)
        {
            if (entry.Id <= 0 || !seen.Add(entry.Id))
            {
                while (!seen.Add(fallbackId))
                    fallbackId++;
                entry.Id = fallbackId++;
            }
        }

        return result;
    }

    private AppSettingsDocument Clone(AppSettingsDocument doc)
    {
        var json = JsonSerializer.Serialize(doc, _jsonOptions);
        return JsonSerializer.Deserialize<AppSettingsDocument>(json, _jsonOptions) ?? new AppSettingsDocument();
    }
}
