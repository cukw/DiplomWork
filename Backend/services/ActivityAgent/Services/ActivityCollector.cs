using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using ActivityService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ActivityAgent.Services;

public interface IActivityCollector
{
    Task<IReadOnlyList<ActivityReply>> CollectActivitiesAsync(CancellationToken cancellationToken = default);
}

public class ActivityCollector : IActivityCollector
{
    private static readonly string[] SuspiciousProcessMarkers =
    {
        "mimikatz", "keylogger", "hacktool", "malware", "cryptominer", "nc", "netcat"
    };

    private static readonly string[] SensitiveFileMarkers =
    {
        "password", "credential", "secret", "token", "wallet", "private"
    };

    private readonly ILogger<ActivityCollector> _logger;
    private readonly int _computerId;
    private readonly int _maxProcessSamples;
    private readonly int _maxNetworkSamples;
    private readonly int _maxFileSamples;

    public ActivityCollector(ILogger<ActivityCollector> logger, IConfiguration configuration)
    {
        _logger = logger;
        _computerId = configuration.GetValue<int>("Agent:ComputerId", 1);
        _maxProcessSamples = Math.Clamp(configuration.GetValue<int>("Agent:MaxProcessSamples", 20), 1, 200);
        _maxNetworkSamples = Math.Clamp(configuration.GetValue<int>("Agent:MaxNetworkSamples", 20), 1, 200);
        _maxFileSamples = Math.Clamp(configuration.GetValue<int>("Agent:MaxFileSamples", 20), 1, 200);
    }

    public async Task<IReadOnlyList<ActivityReply>> CollectActivitiesAsync(CancellationToken cancellationToken = default)
    {
        var activities = new List<ActivityReply>();

        try
        {
            activities.AddRange(await CollectProcessActivitiesAsync(cancellationToken));
            activities.AddRange(await CollectNetworkActivitiesAsync(cancellationToken));
            activities.AddRange(await CollectFileActivitiesAsync(cancellationToken));

            if (activities.Count > 0)
                return activities;

            return new[]
            {
                BuildActivity(
                    activityType: "SYSTEM_HEARTBEAT",
                    details: new { message = "No observable endpoint events in current cycle" },
                    riskScore: 0,
                    isBlocked: false)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting activity batch");
            return new[]
            {
                BuildActivity(
                    activityType: "COLLECTION_ERROR",
                    details: new { error = ex.Message },
                    riskScore: 0,
                    isBlocked: false)
            };
        }
    }

    private Task<List<ActivityReply>> CollectProcessActivitiesAsync(CancellationToken cancellationToken)
    {
        var activities = new List<ActivityReply>();

        try
        {
            var processes = Process.GetProcesses()
                .OrderByDescending(p =>
                {
                    try { return p.WorkingSet64; }
                    catch { return 0; }
                })
                .Take(_maxProcessSamples);

            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (string.IsNullOrWhiteSpace(process.ProcessName))
                        continue;

                    var normalizedName = process.ProcessName.Trim().ToLowerInvariant();
                    var suspicious = SuspiciousProcessMarkers.Any(marker => normalizedName.Contains(marker, StringComparison.Ordinal));

                    var riskScore = suspicious ? 92f : process.WorkingSet64 > 1_000_000_000 ? 35f : 5f;
                    var isBlocked = suspicious;
                    DateTime? startTimeUtc = null;

                    try
                    {
                        startTimeUtc = process.StartTime.ToUniversalTime();
                    }
                    catch
                    {
                        // Access can be denied for system processes.
                    }

                    activities.Add(BuildActivity(
                        activityType: "PROCESS_SNAPSHOT",
                        details: new
                        {
                            processId = process.Id,
                            processName = process.ProcessName,
                            startTimeUtc,
                            workingSetBytes = process.WorkingSet64
                        },
                        riskScore: riskScore,
                        isBlocked: isBlocked,
                        processName: process.ProcessName));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to collect details for process");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting process activities");
        }

        return Task.FromResult(activities);
    }

    private Task<List<ActivityReply>> CollectNetworkActivitiesAsync(CancellationToken cancellationToken)
    {
        var activities = new List<ActivityReply>();

        try
        {
            var suspiciousPorts = new HashSet<int> { 4444, 1337, 31337, 6667 };
            var connections = IPGlobalProperties
                .GetIPGlobalProperties()
                .GetActiveTcpConnections()
                .Where(c => c.RemoteEndPoint.Address != IPAddress.Any &&
                            c.RemoteEndPoint.Address != IPAddress.IPv6Any &&
                            !IPAddress.IsLoopback(c.RemoteEndPoint.Address))
                .GroupBy(c => new
                {
                    LocalAddress = c.LocalEndPoint.Address.ToString(),
                    LocalPort = c.LocalEndPoint.Port,
                    RemoteAddress = c.RemoteEndPoint.Address.ToString(),
                    RemotePort = c.RemoteEndPoint.Port,
                    State = c.State.ToString()
                })
                .Select(g => g.Key)
                .Take(_maxNetworkSamples);

            foreach (var connection in connections)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remoteAddress = IPAddress.Parse(connection.RemoteAddress);
                var suspiciousPort = suspiciousPorts.Contains(connection.RemotePort);
                var externalAddress = !IsPrivateAddress(remoteAddress);
                var riskScore = suspiciousPort ? 88f : externalAddress ? 20f : 8f;

                activities.Add(BuildActivity(
                    activityType: "NETWORK_CONNECTION",
                    details: new
                    {
                        localAddress = connection.LocalAddress,
                        localPort = connection.LocalPort,
                        remoteAddress = connection.RemoteAddress,
                        remotePort = connection.RemotePort,
                        state = connection.State
                    },
                    riskScore: riskScore,
                    isBlocked: suspiciousPort,
                    url: $"tcp://{connection.RemoteAddress}:{connection.RemotePort}"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting network activities");
        }

        return Task.FromResult(activities);
    }

    private Task<List<ActivityReply>> CollectFileActivitiesAsync(CancellationToken cancellationToken)
    {
        var activities = new List<ActivityReply>();

        try
        {
            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfilePath) || !Directory.Exists(userProfilePath))
                return Task.FromResult(activities);

            var candidateDirectories = new[]
            {
                Path.Combine(userProfilePath, "Desktop"),
                Path.Combine(userProfilePath, "Documents"),
                Path.Combine(userProfilePath, "Downloads")
            }.Where(Directory.Exists);

            var cutoffUtc = DateTime.UtcNow.AddMinutes(-30);
            var recentFiles = new List<FileInfo>();

            foreach (var directory in candidateDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var files = Directory
                        .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                        .Select(path => new FileInfo(path))
                        .Where(file => file.Exists && file.LastWriteTimeUtc >= cutoffUtc)
                        .OrderByDescending(file => file.LastWriteTimeUtc)
                        .Take(_maxFileSamples);

                    recentFiles.AddRange(files);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Skipping file activity collection for directory {Directory}", directory);
                }
            }

            foreach (var file in recentFiles
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(_maxFileSamples))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileName = file.Name.ToLowerInvariant();
                var extension = file.Extension.ToLowerInvariant();

                var sensitiveName = SensitiveFileMarkers.Any(marker => fileName.Contains(marker, StringComparison.Ordinal));
                var sensitiveExtension = extension is ".pem" or ".key" or ".pfx" or ".kdbx";

                var riskScore = (sensitiveName || sensitiveExtension) ? 80f : 10f;

                activities.Add(BuildActivity(
                    activityType: "FILE_TOUCH",
                    details: new
                    {
                        filePath = file.FullName,
                        fileName = file.Name,
                        extension,
                        sizeBytes = file.Length,
                        lastWriteUtc = file.LastWriteTimeUtc
                    },
                    riskScore: riskScore,
                    isBlocked: false));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting file activities");
        }

        return Task.FromResult(activities);
    }

    private ActivityReply BuildActivity(
        string activityType,
        object details,
        float riskScore,
        bool isBlocked,
        string? processName = null,
        string? url = null)
    {
        return new ActivityReply
        {
            ComputerId = _computerId,
            ActivityType = activityType,
            ProcessName = processName ?? string.Empty,
            Url = url ?? string.Empty,
            Details = JsonSerializer.Serialize(details),
            Timestamp = DateTime.UtcNow.ToString("O"),
            RiskScore = riskScore,
            IsBlocked = isBlocked,
            Synced = false
        };
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        return bytes[0] switch
        {
            10 => true,
            172 when bytes[1] >= 16 && bytes[1] <= 31 => true,
            192 when bytes[1] == 168 => true,
            127 => true,
            _ => false
        };
    }
}
