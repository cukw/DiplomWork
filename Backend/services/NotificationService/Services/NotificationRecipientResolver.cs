using Grpc.Core;
using AuthLookup = AuthService;
using UserLookup = UserService;

namespace NotificationService.Services;

public sealed record NotificationRecipientContext(int? UserId, string? Email);

public interface INotificationRecipientResolver
{
    Task<NotificationRecipientContext> ResolveByComputerIdAsync(int computerId, CancellationToken cancellationToken);
    Task<NotificationRecipientContext> ResolveByUserIdAsync(int userId, CancellationToken cancellationToken);
}

public sealed class NotificationRecipientResolver : INotificationRecipientResolver
{
    private readonly UserLookup.UserService.UserServiceClient _userServiceClient;
    private readonly AuthLookup.AuthService.AuthServiceClient _authServiceClient;
    private readonly ILogger<NotificationRecipientResolver> _logger;

    public NotificationRecipientResolver(
        UserLookup.UserService.UserServiceClient userServiceClient,
        AuthLookup.AuthService.AuthServiceClient authServiceClient,
        ILogger<NotificationRecipientResolver> logger)
    {
        _userServiceClient = userServiceClient;
        _authServiceClient = authServiceClient;
        _logger = logger;
    }

    public async Task<NotificationRecipientContext> ResolveByComputerIdAsync(int computerId, CancellationToken cancellationToken)
    {
        if (computerId <= 0)
            return new NotificationRecipientContext(null, null);

        try
        {
            var computerResponse = await _userServiceClient.GetComputerInfoAsync(
                new UserLookup.GetComputerInfoRequest { ComputerId = computerId },
                cancellationToken: cancellationToken);

            if (!computerResponse.Success || computerResponse.Computer is null || computerResponse.Computer.Id <= 0)
            {
                _logger.LogWarning("Cannot resolve recipient: computer {ComputerId} not found in UserService", computerId);
                return new NotificationRecipientContext(null, null);
            }

            var userId = (int)computerResponse.Computer.UserId;
            if (userId <= 0)
            {
                _logger.LogWarning("Cannot resolve recipient: computer {ComputerId} has invalid user_id {UserId}", computerId, computerResponse.Computer.UserId);
                return new NotificationRecipientContext(null, null);
            }

            var byUser = await ResolveByUserIdAsync(userId, cancellationToken);
            return byUser with { UserId = userId };
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(
                ex,
                "UserService GetComputerInfo failed while resolving recipient for computer {ComputerId}. Status={StatusCode}",
                computerId,
                ex.StatusCode);
            return new NotificationRecipientContext(null, null);
        }
    }

    public async Task<NotificationRecipientContext> ResolveByUserIdAsync(int userId, CancellationToken cancellationToken)
    {
        if (userId <= 0)
            return new NotificationRecipientContext(null, null);

        try
        {
            var userResponse = await _userServiceClient.GetUserProfileAsync(
                new UserLookup.GetUserProfileRequest { UserId = userId },
                cancellationToken: cancellationToken);

            if (!userResponse.Success || userResponse.UserProfile is null || userResponse.UserProfile.Id <= 0)
            {
                _logger.LogWarning("Cannot resolve recipient: user {UserId} not found in UserService", userId);
                return new NotificationRecipientContext(userId, null);
            }

            var authUserId = userResponse.UserProfile.AuthUserId;
            if (authUserId <= 0)
            {
                _logger.LogWarning("Cannot resolve recipient email: user {UserId} has invalid auth_user_id {AuthUserId}", userId, authUserId);
                return new NotificationRecipientContext(userId, null);
            }

            var email = await ResolveEmailByAuthUserIdAsync(authUserId, cancellationToken);
            return new NotificationRecipientContext(userId, email);
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(
                ex,
                "UserService GetUserProfile failed while resolving recipient for user {UserId}. Status={StatusCode}",
                userId,
                ex.StatusCode);
            return new NotificationRecipientContext(userId, null);
        }
    }

    private async Task<string?> ResolveEmailByAuthUserIdAsync(long authUserId, CancellationToken cancellationToken)
    {
        try
        {
            var authResponse = await _authServiceClient.GetUserProfileAsync(
                new AuthLookup.GetUserProfileRequest { UserId = authUserId },
                cancellationToken: cancellationToken);

            if (!authResponse.Success || authResponse.User is null || authResponse.User.Id <= 0)
            {
                _logger.LogWarning("Cannot resolve recipient email: auth user {AuthUserId} not found in AuthService", authUserId);
                return null;
            }

            var email = NormalizeEmail(authResponse.User.Email);
            if (email is null)
            {
                _logger.LogWarning("Cannot resolve recipient email: auth user {AuthUserId} has empty email", authUserId);
            }

            return email;
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(
                ex,
                "AuthService GetUserProfile failed while resolving email for auth user {AuthUserId}. Status={StatusCode}",
                authUserId,
                ex.StatusCode);
            return null;
        }
    }

    private static string? NormalizeEmail(string? rawEmail)
    {
        if (string.IsNullOrWhiteSpace(rawEmail))
            return null;

        var normalized = rawEmail.Trim();
        return normalized.Contains('@', StringComparison.Ordinal) ? normalized : null;
    }
}
