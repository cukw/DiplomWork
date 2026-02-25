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
            await SaveUnsafeAsync(sanitized, cancellationToken);
            return Clone(sanitized);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<ApplicationListEntryModel>> GetWhitelistEntriesAsync(CancellationToken cancellationToken = default)
    {
        var doc = await GetAsync(cancellationToken);
        return Clone(doc).WhitelistEntries;
    }

    public async Task<List<ApplicationListEntryModel>> GetBlacklistEntriesAsync(CancellationToken cancellationToken = default)
    {
        var doc = await GetAsync(cancellationToken);
        return Clone(doc).BlacklistEntries;
    }

    public Task<List<ApplicationListEntryModel>> ReplaceWhitelistEntriesAsync(List<ApplicationListEntryModel> entries, CancellationToken cancellationToken = default)
        => ReplaceListAsync(entries, isWhitelist: true, cancellationToken);

    public Task<List<ApplicationListEntryModel>> ReplaceBlacklistEntriesAsync(List<ApplicationListEntryModel> entries, CancellationToken cancellationToken = default)
        => ReplaceListAsync(entries, isWhitelist: false, cancellationToken);

    public Task<List<ApplicationListEntryModel>> UpsertWhitelistEntryAsync(ApplicationListEntryModel entry, CancellationToken cancellationToken = default)
        => UpsertListEntryAsync(entry, isWhitelist: true, cancellationToken);

    public Task<List<ApplicationListEntryModel>> UpsertBlacklistEntryAsync(ApplicationListEntryModel entry, CancellationToken cancellationToken = default)
        => UpsertListEntryAsync(entry, isWhitelist: false, cancellationToken);

    public Task<List<ApplicationListEntryModel>> DeleteWhitelistEntryAsync(long id, CancellationToken cancellationToken = default)
        => DeleteListEntryAsync(id, isWhitelist: true, cancellationToken);

    public Task<List<ApplicationListEntryModel>> DeleteBlacklistEntryAsync(long id, CancellationToken cancellationToken = default)
        => DeleteListEntryAsync(id, isWhitelist: false, cancellationToken);

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

    private async Task<List<ApplicationListEntryModel>> ReplaceListAsync(
        List<ApplicationListEntryModel> entries,
        bool isWhitelist,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var doc = await LoadUnsafeAsync(cancellationToken);
            if (isWhitelist)
                doc.WhitelistEntries = entries ?? [];
            else
                doc.BlacklistEntries = entries ?? [];

            var saved = await SaveMutatedUnsafeAsync(doc, cancellationToken);
            return CloneList(isWhitelist ? saved.WhitelistEntries : saved.BlacklistEntries);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<ApplicationListEntryModel>> UpsertListEntryAsync(
        ApplicationListEntryModel entry,
        bool isWhitelist,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var doc = await LoadUnsafeAsync(cancellationToken);
            var list = isWhitelist ? doc.WhitelistEntries : doc.BlacklistEntries;
            if (isWhitelist)
                doc.WhitelistEntries = list;
            else
                doc.BlacklistEntries = list;

            var normalizedApplication = (entry.Application ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedApplication))
                throw new ArgumentException("Application is required", nameof(entry));

            var normalizedDescription = (entry.Description ?? string.Empty).Trim();
            var existingIndex = entry.Id > 0 ? list.FindIndex(x => x.Id == entry.Id) : -1;
            if (existingIndex >= 0)
            {
                list[existingIndex] = new ApplicationListEntryModel
                {
                    Id = entry.Id,
                    Application = normalizedApplication,
                    Description = normalizedDescription
                };
            }
            else
            {
                list.Add(new ApplicationListEntryModel
                {
                    Id = entry.Id,
                    Application = normalizedApplication,
                    Description = normalizedDescription
                });
            }

            var saved = await SaveMutatedUnsafeAsync(doc, cancellationToken);
            return CloneList(isWhitelist ? saved.WhitelistEntries : saved.BlacklistEntries);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<ApplicationListEntryModel>> DeleteListEntryAsync(
        long id,
        bool isWhitelist,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var doc = await LoadUnsafeAsync(cancellationToken);
            var list = isWhitelist ? doc.WhitelistEntries : doc.BlacklistEntries;
            list = (list ?? []).Where(x => x.Id != id).ToList();
            if (isWhitelist)
                doc.WhitelistEntries = list;
            else
                doc.BlacklistEntries = list;

            var saved = await SaveMutatedUnsafeAsync(doc, cancellationToken);
            return CloneList(isWhitelist ? saved.WhitelistEntries : saved.BlacklistEntries);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AppSettingsDocument> SaveMutatedUnsafeAsync(AppSettingsDocument incoming, CancellationToken cancellationToken)
    {
        var sanitized = Sanitize(incoming);
        sanitized.UpdatedAt = DateTime.UtcNow;
        await SaveUnsafeAsync(sanitized, cancellationToken);
        return sanitized;
    }

    private async Task SaveUnsafeAsync(AppSettingsDocument document, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(document, _jsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json, cancellationToken);
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

    private List<ApplicationListEntryModel> CloneList(List<ApplicationListEntryModel> source)
    {
        var json = JsonSerializer.Serialize(source ?? [], _jsonOptions);
        return JsonSerializer.Deserialize<List<ApplicationListEntryModel>>(json, _jsonOptions) ?? [];
    }
}
