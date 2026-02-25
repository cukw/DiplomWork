namespace Gateway.Services;

public sealed class DownloadFileStore
{
    private readonly string _root;

    public DownloadFileStore(IHostEnvironment environment)
    {
        _root = Path.Combine(environment.ContentRootPath, "data", "exports");
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(string preferredFileName, byte[] content, CancellationToken cancellationToken = default)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));

        var safeBase = Path.GetFileName(preferredFileName);
        if (string.IsNullOrWhiteSpace(safeBase))
            safeBase = $"export_{DateTime.UtcNow:yyyyMMddHHmmss}.dat";

        safeBase = string.Concat(safeBase.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_'));

        var finalName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}_{safeBase}";
        var path = Path.Combine(_root, finalName);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        return finalName;
    }

    public bool TryResolve(string fileName, out string fullPath)
    {
        fullPath = string.Empty;
        var safe = Path.GetFileName(fileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(safe))
            return false;

        var candidate = Path.Combine(_root, safe);
        if (!File.Exists(candidate))
            return false;

        fullPath = candidate;
        return true;
    }
}
