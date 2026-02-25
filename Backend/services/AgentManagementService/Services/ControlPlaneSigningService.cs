using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ProtoAgentCommand = global::AgentManagementService.AgentCommand;
using ProtoAgentPolicy = global::AgentManagementService.AgentPolicy;

namespace AgentManagementService.Services;

public sealed class ControlPlaneSigningService
{
    public const string Algorithm = "hmac-sha256-v1";

    private readonly ILogger<ControlPlaneSigningService> _logger;
    private readonly byte[]? _secretBytes;
    private bool _disabledLogged;

    public string KeyId { get; }
    public bool Enabled => _secretBytes is { Length: > 0 };

    public ControlPlaneSigningService(IConfiguration configuration, ILogger<ControlPlaneSigningService> logger)
    {
        _logger = logger;
        KeyId = string.IsNullOrWhiteSpace(configuration["AgentSigning:KeyId"])
            ? "default"
            : configuration["AgentSigning:KeyId"]!.Trim();

        var secret = configuration["AgentSigning:Secret"];
        if (!string.IsNullOrWhiteSpace(secret))
        {
            _secretBytes = Encoding.UTF8.GetBytes(secret.Trim());
            _logger.LogInformation("Control-plane signing enabled (keyId={KeyId}, alg={Alg})", KeyId, Algorithm);
        }
        else
        {
            _secretBytes = null;
            _logger.LogWarning("Control-plane signing is disabled (AgentSigning:Secret not configured)");
        }
    }

    public void ApplyPolicySignature(ProtoAgentPolicy policy)
    {
        if (!EnsureEnabled())
            return;

        policy.SignatureKeyId = KeyId;
        policy.SignatureAlg = Algorithm;
        policy.Signature = ComputeHex(BuildCanonicalPolicy(policy));
    }

    public void ApplyCommandSignature(ProtoAgentCommand command)
    {
        if (!EnsureEnabled())
            return;

        command.SignatureKeyId = KeyId;
        command.SignatureAlg = Algorithm;
        command.Signature = ComputeHex(BuildCanonicalCommand(command));
    }

    private bool EnsureEnabled()
    {
        if (Enabled)
            return true;

        if (!_disabledLogged)
        {
            _disabledLogged = true;
            _logger.LogDebug("Skipping control-plane signature because signing is disabled");
        }
        return false;
    }

    private string ComputeHex(byte[] payload)
    {
        if (_secretBytes is null)
            return string.Empty;

        using var hmac = new HMACSHA256(_secretBytes);
        var hash = hmac.ComputeHash(payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] BuildCanonicalPolicy(ProtoAgentPolicy p)
    {
        var sb = new StringBuilder(512);
        AppendString(sb, "kind", "policy");
        AppendInt64(sb, "id", p.Id);
        AppendInt64(sb, "agent_id", p.AgentId);
        AppendInt64(sb, "computer_id", p.ComputerId);
        AppendString(sb, "policy_version", p.PolicyVersion);
        AppendInt32(sb, "collection_interval_sec", p.CollectionIntervalSec);
        AppendInt32(sb, "heartbeat_interval_sec", p.HeartbeatIntervalSec);
        AppendInt32(sb, "flush_interval_sec", p.FlushIntervalSec);
        AppendBool(sb, "enable_process_collection", p.EnableProcessCollection);
        AppendBool(sb, "enable_browser_collection", p.EnableBrowserCollection);
        AppendBool(sb, "enable_active_window_collection", p.EnableActiveWindowCollection);
        AppendBool(sb, "enable_idle_collection", p.EnableIdleCollection);
        AppendInt32(sb, "idle_threshold_sec", p.IdleThresholdSec);
        AppendInt32(sb, "browser_poll_interval_sec", p.BrowserPollIntervalSec);
        AppendInt32(sb, "process_snapshot_limit", p.ProcessSnapshotLimit);
        AppendFloat32Bits(sb, "high_risk_threshold_f32bits", p.HighRiskThreshold);
        AppendBool(sb, "auto_lock_enabled", p.AutoLockEnabled);
        AppendBool(sb, "admin_blocked", p.AdminBlocked);
        AppendString(sb, "blocked_reason", p.BlockedReason);
        AppendString(sb, "updated_at", p.UpdatedAt);
        AppendStringList(sb, "browsers", p.Browsers);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] BuildCanonicalCommand(ProtoAgentCommand c)
    {
        var sb = new StringBuilder(384);
        AppendString(sb, "kind", "command");
        AppendInt64(sb, "id", c.Id);
        AppendInt64(sb, "agent_id", c.AgentId);
        AppendString(sb, "type", c.Type);
        AppendString(sb, "payload_json", c.PayloadJson);
        AppendString(sb, "status", c.Status);
        AppendString(sb, "requested_by", c.RequestedBy);
        AppendString(sb, "result_message", c.ResultMessage);
        AppendString(sb, "created_at", c.CreatedAt);
        AppendString(sb, "acknowledged_at", c.AcknowledgedAt);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AppendInt32(StringBuilder sb, string key, int value)
    {
        sb.Append(key)
            .Append('=')
            .Append(value.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
    }

    private static void AppendInt64(StringBuilder sb, string key, long value)
    {
        sb.Append(key)
            .Append('=')
            .Append(value.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
    }

    private static void AppendBool(StringBuilder sb, string key, bool value)
    {
        sb.Append(key)
            .Append('=')
            .Append(value ? '1' : '0')
            .Append('\n');
    }

    private static void AppendFloat32Bits(StringBuilder sb, string key, float value)
    {
        sb.Append(key)
            .Append('=')
            .Append(BitConverter.SingleToInt32Bits(value).ToString(CultureInfo.InvariantCulture))
            .Append('\n');
    }

    private static void AppendString(StringBuilder sb, string key, string? value)
    {
        var safe = value ?? string.Empty;
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(safe));
        sb.Append(key)
            .Append('=')
            .Append(b64)
            .Append('\n');
    }

    private static void AppendStringList(StringBuilder sb, string key, IEnumerable<string> values)
    {
        var arr = values?.ToArray() ?? [];
        sb.Append(key)
            .Append("_count=")
            .Append(arr.Length.ToString(CultureInfo.InvariantCulture))
            .Append('\n');

        for (var i = 0; i < arr.Length; i++)
            AppendString(sb, $"{key}_{i}", arr[i]);
    }
}
