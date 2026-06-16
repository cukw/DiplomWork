using System.Globalization;
using Gateway.Models;
using Gateway.Protos.Agent;
using Grpc.Core;
using AgentClient = Gateway.Protos.Agent.AgentManagementService.AgentManagementServiceClient;

namespace Gateway.Services;

public sealed class PolicyAccessListSyncService
{
    private readonly AgentClient _agentClient;
    private readonly ILogger<PolicyAccessListSyncService> _logger;

    public PolicyAccessListSyncService(
        AgentClient agentClient,
        ILogger<PolicyAccessListSyncService> logger)
    {
        _agentClient = agentClient;
        _logger = logger;
    }

    public async Task<PolicyAccessListSyncResult> SyncFromSettingsAsync(
        AppSettingsDocument settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var whitelistApps = NormalizeApplications(settings.WhitelistEntries);
        var blacklistApps = NormalizeApplications(settings.BlacklistEntries);
        var enableWhitelist = settings.MonitoringSettings?.EnableWhitelist ?? true;
        var enableBlacklist = settings.MonitoringSettings?.EnableBlacklist ?? true;

        var agents = await GetAllAgentsAsync(cancellationToken);
        var syncedAgents = 0;
        var failedAgents = 0;
        var errors = new List<string>();

        foreach (var agent in agents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var policy = await GetOrCreatePolicyAsync(agent, cancellationToken);
                policy.EnableWhitelist = enableWhitelist;
                policy.EnableBlacklist = enableBlacklist;
                policy.ComputerId = policy.ComputerId > 0 ? policy.ComputerId : agent.ComputerId;

                policy.WhitelistApps.Clear();
                policy.WhitelistApps.AddRange(whitelistApps);

                policy.BlacklistApps.Clear();
                policy.BlacklistApps.AddRange(blacklistApps);

                if (policy.Browsers.Count == 0)
                    policy.Browsers.AddRange(["chrome", "edge", "firefox"]);

                policy.PolicyVersion = NewPolicyVersion(policy.PolicyVersion);

                var upsertResponse = await _agentClient.UpsertAgentPolicyAsync(
                    new UpsertAgentPolicyRequest { Policy = policy },
                    cancellationToken: cancellationToken);

                if (!upsertResponse.Success)
                {
                    failedAgents++;
                    errors.Add($"agent_id={agent.Id}: {upsertResponse.Message}");
                    continue;
                }

                syncedAgents++;
            }
            catch (RpcException ex)
            {
                failedAgents++;
                errors.Add($"agent_id={agent.Id}: {ex.StatusCode} {ex.Status.Detail}");
            }
            catch (Exception ex)
            {
                failedAgents++;
                errors.Add($"agent_id={agent.Id}: {ex.Message}");
            }
        }

        if (failedAgents > 0)
        {
            _logger.LogWarning(
                "Policy access-list sync finished with errors. Total={Total}, Synced={Synced}, Failed={Failed}",
                agents.Count,
                syncedAgents,
                failedAgents);
        }
        else
        {
            _logger.LogInformation(
                "Policy access-list sync completed. Total={Total}, Synced={Synced}",
                agents.Count,
                syncedAgents);
        }

        return new PolicyAccessListSyncResult
        {
            TotalAgents = agents.Count,
            SyncedAgents = syncedAgents,
            FailedAgents = failedAgents,
            Errors = errors
        };
    }

    private async Task<List<Agent>> GetAllAgentsAsync(CancellationToken cancellationToken)
    {
        const int pageSize = 200;
        var page = 1;
        var result = new List<Agent>();
        var expectedTotal = int.MaxValue;

        while (result.Count < expectedTotal)
        {
            var response = await _agentClient.GetAllAgentsAsync(
                new GetAllAgentsRequest
                {
                    Page = page,
                    PageSize = pageSize,
                    Status = string.Empty
                },
                cancellationToken: cancellationToken);

            if (!response.Success)
                throw new InvalidOperationException($"GetAllAgents failed: {response.Message}");

            expectedTotal = response.TotalCount > 0 ? response.TotalCount : expectedTotal;
            if (response.Agents.Count == 0)
                break;

            result.AddRange(response.Agents);
            if (response.Agents.Count < pageSize)
                break;

            page++;
        }

        return result;
    }

    private async Task<AgentPolicy> GetOrCreatePolicyAsync(Agent agent, CancellationToken cancellationToken)
    {
        var policyResponse = await _agentClient.GetAgentPolicyAsync(
            new GetAgentPolicyRequest { AgentId = agent.Id },
            cancellationToken: cancellationToken);

        if (policyResponse.Success && policyResponse.Policy is not null && policyResponse.Policy.AgentId > 0)
            return policyResponse.Policy;

        return BuildDefaultPolicy(agent);
    }

    private static AgentPolicy BuildDefaultPolicy(Agent agent)
    {
        var policy = new AgentPolicy
        {
            AgentId = agent.Id,
            ComputerId = agent.ComputerId,
            PolicyVersion = NewPolicyVersion(),
            CollectionIntervalSec = 5,
            HeartbeatIntervalSec = 15,
            FlushIntervalSec = 5,
            EnableProcessCollection = true,
            EnableBrowserCollection = true,
            EnableActiveWindowCollection = true,
            EnableIdleCollection = true,
            IdleThresholdSec = 120,
            BrowserPollIntervalSec = 10,
            ProcessSnapshotLimit = 50,
            HighRiskThreshold = 85,
            AutoLockEnabled = true,
            EnableWhitelist = true,
            EnableBlacklist = true,
            AdminBlocked = false,
            BlockedReason = string.Empty
        };

        policy.Browsers.AddRange(["chrome", "edge", "firefox"]);
        return policy;
    }

    private static string NewPolicyVersion(string? previousVersion = null)
    {
        var candidate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(candidate, previousVersion, StringComparison.Ordinal))
            return candidate;

        var fallback = $"{candidate}-{Guid.NewGuid():N}";
        return fallback.Length <= 50 ? fallback : fallback[..50];
    }

    private static string[] NormalizeApplications(IEnumerable<ApplicationListEntryModel>? entries)
    {
        return (entries ?? [])
            .Select(x => x.Application)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class PolicyAccessListSyncResult
{
    public int TotalAgents { get; init; }
    public int SyncedAgents { get; init; }
    public int FailedAgents { get; init; }
    public List<string> Errors { get; init; } = [];
    public bool Success => FailedAgents == 0;
}
