using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace AgentManagementService.Services;

public sealed class AgentAuthInterceptor : Interceptor
{
    private static readonly HashSet<string> ProtectedMethods = new(StringComparer.Ordinal)
    {
        "/agent.AgentManagementService/RegisterAgent",
        "/agent.AgentManagementService/UpdateAgentStatus",
        "/agent.AgentManagementService/GetAgentsByComputer",
        "/agent.AgentManagementService/GetAgentPolicy",
        "/agent.AgentManagementService/GetPendingAgentCommands",
        "/agent.AgentManagementService/AckAgentCommand",
        "/agent.AgentManagementService/CreateSyncBatch",
        "/agent.AgentManagementService/UpdateSyncBatch",
        "/agent.AgentManagementService/GetSyncBatch",
        "/agent.AgentManagementService/GetSyncBatchesByAgent",
        "/agent.AgentManagementService/GetPendingSyncBatches"
    };

    private readonly ILogger<AgentAuthInterceptor> _logger;
    private readonly string _token;
    private readonly string _headerName;
    private bool _disabledLogged;

    public AgentAuthInterceptor(IConfiguration configuration, ILogger<AgentAuthInterceptor> logger)
    {
        _logger = logger;
        _token = (configuration["AgentAuth:Token"] ?? string.Empty).Trim();
        _headerName = string.IsNullOrWhiteSpace(configuration["AgentAuth:HeaderName"])
            ? "x-agent-token"
            : configuration["AgentAuth:HeaderName"]!.Trim().ToLowerInvariant();
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return await continuation(request, context);
    }

    private void EnsureAuthorized(ServerCallContext context)
    {
        if (!ProtectedMethods.Contains(context.Method))
            return;

        if (string.IsNullOrWhiteSpace(_token))
        {
            if (!_disabledLogged)
            {
                _disabledLogged = true;
                _logger.LogWarning("Agent token auth is disabled for AgentManagementService (AgentAuth:Token is empty).");
            }
            return;
        }

        var header = context.RequestHeaders.FirstOrDefault(h => string.Equals(h.Key, _headerName, StringComparison.OrdinalIgnoreCase));
        var provided = header?.Value ?? string.Empty;
        if (!SecureEquals(provided, _token))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing or invalid agent token"));
    }

    private static bool SecureEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
