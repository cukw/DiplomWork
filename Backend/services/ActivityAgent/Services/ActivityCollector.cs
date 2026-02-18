using System.Diagnostics;
using System.Management;
using System.Text.Json;
using ActivityService;

namespace ActivityAgent.Services;

public interface IActivityCollector
{
    Task<ActivityReply> CollectActivityAsync();
}

public class ActivityCollector : IActivityCollector
{
    private readonly ILogger<ActivityCollector> _logger;
    private readonly IConfiguration _configuration;
    private readonly int _computerId;

    public ActivityCollector(ILogger<ActivityCollector> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _computerId = _configuration.GetValue<int>("Agent:ComputerId", 1);
    }

    public async Task<ActivityReply> CollectActivityAsync()
    {
        try
        {
            var activities = new List<ActivityReply>();
            
            // Collect running processes
            var processActivities = await CollectProcessActivitiesAsync();
            activities.AddRange(processActivities);
            
            // Collect network activity
            var networkActivities = await CollectNetworkActivitiesAsync();
            activities.AddRange(networkActivities);
            
            // Collect file system activity
            var fileActivities = await CollectFileActivitiesAsync();
            activities.AddRange(fileActivities);
            
            // Return a random activity for this demo
            // In a real implementation, you would return all activities
            if (activities.Any())
            {
                var random = new Random();
                var selectedActivity = activities[random.Next(activities.Count)];
                return selectedActivity;
            }
            
            // Return a default activity if nothing was collected
            return new ActivityReply
            {
                ComputerId = _computerId,
                ActivityType = "SYSTEM_HEARTBEAT",
                Details = JsonSerializer.Serialize(new { message = "System heartbeat" }),
                Timestamp = DateTime.UtcNow.ToString("O"),
                RiskScore = 0,
                IsBlocked = false,
                Synced = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting activity");
            return new ActivityReply
            {
                ComputerId = _computerId,
                ActivityType = "COLLECTION_ERROR",
                Details = JsonSerializer.Serialize(new { error = ex.Message }),
                Timestamp = DateTime.UtcNow.ToString("O"),
                RiskScore = 0,
                IsBlocked = false,
                Synced = false
            };
        }
    }

    private async Task<List<ActivityReply>> CollectProcessActivitiesAsync()
    {
        var activities = new List<ActivityReply>();
        
        try
        {
            var processes = Process.GetProcesses();
            var suspiciousProcesses = new[] { "hacktool.exe", "keylogger.exe", "malware.exe", "cryptominer.exe" };
            
            foreach (var process in processes.Take(10)) // Limit to 10 processes for demo
            {
                try
                {
                    if (string.IsNullOrEmpty(process.ProcessName))
                        continue;
                        
                    var riskScore = 0f;
                    var isBlocked = false;
                    
                    // Check for suspicious processes
                    if (suspiciousProcesses.Contains(process.ProcessName.ToLower()))
                    {
                        riskScore = 90f;
                        isBlocked = true;
                    }
                    
                    activities.Add(new ActivityReply
                    {
                        ComputerId = _computerId,
                        ActivityType = "PROCESS_OPEN",
                        ProcessName = process.ProcessName,
                        Details = JsonSerializer.Serialize(new { 
                            processId = process.Id,
                            startTime = process.StartTime.ToString("O"),
                            workingSet = process.WorkingSet64
                        }),
                        Timestamp = DateTime.UtcNow.ToString("O"),
                        RiskScore = riskScore,
                        IsBlocked = isBlocked,
                        Synced = false
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error collecting process info for {ProcessName}", process.ProcessName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting process activities");
        }
        
        return activities;
    }

    private async Task<List<ActivityReply>> CollectNetworkActivitiesAsync()
    {
        var activities = new List<ActivityReply>();
        
        try
        {
            // Simulate network activity collection
            // In a real implementation, you would use network monitoring APIs
            var suspiciousDomains = new[] { "malware.com", "phishing.site", "suspicious.net" };
            var domains = new[] { "google.com", "github.com", "stackoverflow.com", "malware.com" };
            
            foreach (var domain in domains)
            {
                var riskScore = 0f;
                var isBlocked = false;
                
                if (suspiciousDomains.Contains(domain))
                {
                    riskScore = 85f;
                    isBlocked = true;
                }
                
                activities.Add(new ActivityReply
                {
                    ComputerId = _computerId,
                    ActivityType = "NETWORK_ACCESS",
                    Url = $"https://{domain}",
                    Details = JsonSerializer.Serialize(new { 
                        domain = domain,
                        protocol = "HTTPS",
                        timestamp = DateTime.UtcNow.ToString("O")
                    }),
                    Timestamp = DateTime.UtcNow.ToString("O"),
                    RiskScore = riskScore,
                    IsBlocked = isBlocked,
                    Synced = false
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting network activities");
        }
        
        return activities;
    }

    private async Task<List<ActivityReply>> CollectFileActivitiesAsync()
    {
        var activities = new List<ActivityReply>();
        
        try
        {
            // Simulate file activity collection
            // In a real implementation, you would use file system monitoring APIs
            var suspiciousFiles = new[] { "passwords.txt", "credit_cards.csv", "sensitive_data.docx" };
            var files = new[] { 
                @"C:\Users\Public\Documents\report.pdf", 
                @"C:\Temp\log.txt", 
                @"C:\Users\Public\Documents\passwords.txt" 
            };
            
            foreach (var file in files)
            {
                var riskScore = 0f;
                var isBlocked = false;
                var fileName = Path.GetFileName(file);
                
                if (suspiciousFiles.Contains(fileName))
                {
                    riskScore = 95f;
                    isBlocked = true;
                }
                
                activities.Add(new ActivityReply
                {
                    ComputerId = _computerId,
                    ActivityType = "FILE_ACCESS",
                    Details = JsonSerializer.Serialize(new { 
                        filePath = file,
                        fileName = fileName,
                        timestamp = DateTime.UtcNow.ToString("O")
                    }),
                    Timestamp = DateTime.UtcNow.ToString("O"),
                    RiskScore = riskScore,
                    IsBlocked = isBlocked,
                    Synced = false
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting file activities");
        }
        
        return activities;
    }
}