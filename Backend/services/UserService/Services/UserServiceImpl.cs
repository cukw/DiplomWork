using Grpc.Core;
using UserService.Data;
using UserService.Models;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace UserService.Services;

public class UserServiceImpl : UserService.UserServiceBase
{
    private static readonly TimeSpan MoscowUtcOffset = TimeSpan.FromHours(3);

    private readonly UserDbContext _db;
    private readonly ILogger<UserServiceImpl> _logger;

    public UserServiceImpl(
        UserDbContext db,
        ILogger<UserServiceImpl> logger)
    {
        _db = db;
        _logger = logger;
    }

    public override async Task<GetUserProfileResponse> GetUserProfile(GetUserProfileRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get user profile request for user ID: {UserId}", request.UserId);

        try
        {
            var user = await _db.Users
                .Include(u => u.Computers)
                .FirstOrDefaultAsync(u => u.Id == request.UserId);

            if (user == null)
            {
                return new GetUserProfileResponse
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            return new GetUserProfileResponse
            {
                Success = true,
                Message = "User profile retrieved successfully",
                UserProfile = MapUserToProto(user)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user profile for ID: {UserId}", request.UserId);
            return new GetUserProfileResponse
            {
                Success = false,
                Message = "An error occurred while retrieving user profile"
            };
        }
    }

    public override async Task<UpdateUserProfileResponse> UpdateUserProfile(UpdateUserProfileRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Update user profile request for user ID: {UserId}", request.UserId);

        try
        {
            var user = await _db.Users
                .Include(u => u.Computers)
                .FirstOrDefaultAsync(u => u.Id == request.UserId);

            if (user == null)
            {
                return new UpdateUserProfileResponse
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            // Update user properties
            if (!string.IsNullOrEmpty(request.FullName))
                user.FullName = request.FullName;
            
            if (!string.IsNullOrEmpty(request.Department))
                user.Department = request.Department;

            await _db.SaveChangesAsync();

            return new UpdateUserProfileResponse
            {
                Success = true,
                Message = "User profile updated successfully",
                UserProfile = MapUserToProto(user)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user profile for ID: {UserId}", request.UserId);
            return new UpdateUserProfileResponse
            {
                Success = false,
                Message = "An error occurred while updating user profile"
            };
        }
    }

    public override async Task<GetComputerInfoResponse> GetComputerInfo(GetComputerInfoRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get computer info request for computer ID: {ComputerId}", request.ComputerId);

        try
        {
            var computer = await _db.Computers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == request.ComputerId);

            if (computer == null)
            {
                return new GetComputerInfoResponse
                {
                    Success = false,
                    Message = "Computer not found"
                };
            }

            return new GetComputerInfoResponse
            {
                Success = true,
                Message = "Computer info retrieved successfully",
                Computer = MapComputerToProto(computer)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving computer info for ID: {ComputerId}", request.ComputerId);
            return new GetComputerInfoResponse
            {
                Success = false,
                Message = "An error occurred while retrieving computer info"
            };
        }
    }

    public override async Task<UpdateComputerInfoResponse> UpdateComputerInfo(UpdateComputerInfoRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Update computer info request for computer ID: {ComputerId}", request.ComputerId);

        try
        {
            var computer = await _db.Computers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == request.ComputerId);

            if (computer == null)
            {
                return new UpdateComputerInfoResponse
                {
                    Success = false,
                    Message = "Computer not found"
                };
            }

            var requestedHostname = request.Hostname?.Trim();
            if (!string.IsNullOrWhiteSpace(requestedHostname) &&
                !string.Equals(requestedHostname, computer.Hostname, StringComparison.OrdinalIgnoreCase))
            {
                var hostnameUsed = await _db.Computers.AnyAsync(c => c.Id != computer.Id && c.Hostname == requestedHostname);
                if (hostnameUsed)
                {
                    return new UpdateComputerInfoResponse
                    {
                        Success = false,
                        Message = "Hostname is already used by another computer"
                    };
                }
            }

            var requestedMac = request.MacAddress?.Trim();
            if (!string.IsNullOrWhiteSpace(requestedMac) &&
                !string.Equals(requestedMac, computer.MacAddress, StringComparison.OrdinalIgnoreCase))
            {
                var macUsed = await _db.Computers.AnyAsync(c => c.Id != computer.Id && c.MacAddress == requestedMac);
                if (macUsed)
                {
                    return new UpdateComputerInfoResponse
                    {
                        Success = false,
                        Message = "MAC address is already used by another computer"
                    };
                }
            }

            // Update computer properties
            if (!string.IsNullOrEmpty(request.Hostname))
                computer.Hostname = requestedHostname!;
            
            if (!string.IsNullOrEmpty(request.OsVersion))
                computer.OsVersion = request.OsVersion;
            
            if (!string.IsNullOrEmpty(request.IpAddress))
                computer.IpAddress = request.IpAddress;
            
            if (!string.IsNullOrEmpty(request.MacAddress))
                computer.MacAddress = requestedMac;
            
            if (!string.IsNullOrEmpty(request.Status))
                computer.Status = request.Status;

            computer.LastSeen = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return new UpdateComputerInfoResponse
            {
                Success = true,
                Message = "Computer info updated successfully",
                Computer = MapComputerToProto(computer)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating computer info for ID: {ComputerId}", request.ComputerId);
            return new UpdateComputerInfoResponse
            {
                Success = false,
                Message = "An error occurred while updating computer info"
            };
        }
    }

    public override async Task<GetUsersByDepartmentResponse> GetUsersByDepartment(GetUsersByDepartmentRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get users by department request for department: {Department}", request.Department);

        try
        {
            var users = await _db.Users
                .Include(u => u.Computers)
                .Where(u => u.Department == request.Department)
                .ToListAsync();

            var userProfiles = users.Select(MapUserToProto).ToList();

            return new GetUsersByDepartmentResponse
            {
                Success = true,
                Message = "Users retrieved successfully",
                Users = { userProfiles }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users by department: {Department}", request.Department);
            return new GetUsersByDepartmentResponse
            {
                Success = false,
                Message = "An error occurred while retrieving users"
            };
        }
    }

    public override async Task<GetAllUsersResponse> GetAllUsers(GetAllUsersRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get all users request - Page: {Page}, PageSize: {PageSize}", request.Page, request.PageSize);

        try
        {
            var page = request.Page > 0 ? request.Page : 1;
            var pageSize = request.PageSize > 0 ? request.PageSize : 10;
            
            var query = _db.Users.Include(u => u.Computers).AsQueryable();
            
            var totalCount = await query.CountAsync();
            var users = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var userProfiles = users.Select(MapUserToProto).ToList();

            return new GetAllUsersResponse
            {
                Success = true,
                Message = "Users retrieved successfully",
                Users = { userProfiles },
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all users");
            return new GetAllUsersResponse
            {
                Success = false,
                Message = "An error occurred while retrieving users"
            };
        }
    }

    public override async Task<CreateUserResponse> CreateUser(CreateUserRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Create user request for auth user ID: {AuthUserId}", request.AuthUserId);

        try
        {
            if (request.AuthUserId <= 0)
            {
                return new CreateUserResponse
                {
                    Success = false,
                    Message = "AuthUserId must be greater than 0"
                };
            }

            var hostname = request.Hostname?.Trim() ?? string.Empty;
            var normalizedMac = string.IsNullOrWhiteSpace(request.MacAddress) ? string.Empty : request.MacAddress.Trim();
            var shouldCreateComputer = !string.IsNullOrWhiteSpace(hostname);

            // Check if user already exists
            var existingUser = await _db.Users
                .FirstOrDefaultAsync(u => u.AuthUserId == request.AuthUserId);

            if (existingUser != null)
            {
                return new CreateUserResponse
                {
                    Success = false,
                    Message = "User already exists"
                };
            }

            if (shouldCreateComputer && await _db.Computers.AnyAsync(c => c.Hostname == hostname))
            {
                return new CreateUserResponse
                {
                    Success = false,
                    Message = "Computer hostname is already assigned to another user"
                };
            }

            if (shouldCreateComputer && !string.IsNullOrWhiteSpace(normalizedMac) && await _db.Computers.AnyAsync(c => c.MacAddress == normalizedMac))
            {
                return new CreateUserResponse
                {
                    Success = false,
                    Message = "Computer MAC address is already assigned to another user"
                };
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(context.CancellationToken);

            var user = new User
            {
                AuthUserId = (int)request.AuthUserId,
                FullName = request.FullName,
                Department = request.Department
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync(context.CancellationToken);

            Computer? computer = null;
            if (shouldCreateComputer)
            {
                var now = DateTime.UtcNow;
                var expiresAt = GetCurrentMoscowSessionExpiresAtUtc(now);
                computer = new Computer
                {
                    UserId = user.Id,
                    Hostname = hostname,
                    OsVersion = request.OsVersion,
                    IpAddress = request.IpAddress,
                    MacAddress = normalizedMac,
                    Status = "active",
                    LastSeen = now
                };

                _db.Computers.Add(computer);
                await _db.SaveChangesAsync(context.CancellationToken);

                _db.ComputerSessions.Add(new ComputerSession
                {
                    UserId = user.Id,
                    AuthUserId = (int)request.AuthUserId,
                    ComputerId = computer.Id,
                    StartedAt = now,
                    ExpiresAt = expiresAt,
                    LastSeen = now,
                    Status = "active"
                });
                await _db.SaveChangesAsync(context.CancellationToken);
            }
            await transaction.CommitAsync(context.CancellationToken);

            if (computer != null)
                user.Computers.Add(computer);

            var response = new CreateUserResponse
            {
                Success = true,
                Message = "User created successfully",
                UserProfile = MapUserToProto(user)
            };
            if (computer != null)
                response.Computer = MapComputerToProto(computer);
            return response;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database constraint violation while creating user for auth user ID: {AuthUserId}", request.AuthUserId);
            return new CreateUserResponse
            {
                Success = false,
                Message = "Failed to create user: unique computer/user constraint violated"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user for auth user ID: {AuthUserId}", request.AuthUserId);
            return new CreateUserResponse
            {
                Success = false,
                Message = "An error occurred while creating user"
            };
        }
    }

    public override async Task<EnrollComputerForAuthUserResponse> EnrollComputerForAuthUser(EnrollComputerForAuthUserRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Enroll computer request for auth user ID: {AuthUserId}, hostname: {Hostname}", request.AuthUserId, request.Hostname);

        try
        {
            if (request.AuthUserId <= 0)
            {
                return new EnrollComputerForAuthUserResponse
                {
                    Success = false,
                    Message = "AuthUserId must be greater than 0"
                };
            }

            var hostname = NormalizeRequired(request.Hostname);
            if (string.IsNullOrWhiteSpace(hostname))
            {
                return new EnrollComputerForAuthUserResponse
                {
                    Success = false,
                    Message = "Hostname is required"
                };
            }

            var normalizedMac = NormalizeOptional(request.MacAddress);
            var now = DateTime.UtcNow;
            var expiresAt = GetCurrentMoscowSessionExpiresAtUtc(now);

            await using var transaction = await _db.Database.BeginTransactionAsync(context.CancellationToken);
            await CloseExpiredSessionsAsync(now, context.CancellationToken);

            var user = await _db.Users
                .Include(u => u.Computers)
                .FirstOrDefaultAsync(u => u.AuthUserId == request.AuthUserId, context.CancellationToken);

            var createdUser = false;
            if (user == null)
            {
                user = new User
                {
                    AuthUserId = (int)request.AuthUserId,
                    FullName = string.IsNullOrWhiteSpace(request.FullName) ? $"User {request.AuthUserId}" : request.FullName.Trim(),
                    Department = NormalizeOptional(request.Department)
                };
                _db.Users.Add(user);
                await _db.SaveChangesAsync(context.CancellationToken);
                createdUser = true;
            }

            var activeUserSession = await _db.ComputerSessions
                .Include(s => s.Computer)
                .FirstOrDefaultAsync(s => s.UserId == user.Id && s.EndedAt == null, context.CancellationToken);

            Computer? computer = null;
            if (!string.IsNullOrWhiteSpace(normalizedMac))
            {
                computer = await _db.Computers
                    .Include(c => c.User)
                    .FirstOrDefaultAsync(c => c.MacAddress == normalizedMac, context.CancellationToken);
            }

            computer ??= await _db.Computers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Hostname == hostname, context.CancellationToken);

            if (activeUserSession != null && computer != null && activeUserSession.ComputerId != computer.Id)
            {
                return new EnrollComputerForAuthUserResponse
                {
                    Success = false,
                    Message = $"User already has active session on computer {activeUserSession.ComputerId}"
                };
            }

            if (activeUserSession != null && computer == null)
            {
                return new EnrollComputerForAuthUserResponse
                {
                    Success = false,
                    Message = $"User already has active session on computer {activeUserSession.ComputerId}"
                };
            }

            var createdComputer = false;
            if (computer == null)
            {
                computer = new Computer
                {
                    Hostname = hostname,
                    OsVersion = NormalizeOptional(request.OsVersion),
                    IpAddress = NormalizeOptional(request.IpAddress),
                    MacAddress = normalizedMac,
                    Status = "active",
                    LastSeen = now
                };
                _db.Computers.Add(computer);
                await _db.SaveChangesAsync(context.CancellationToken);
                createdComputer = true;
            }
            else
            {
                var activeComputerSession = await _db.ComputerSessions
                    .Include(s => s.User)
                    .FirstOrDefaultAsync(s => s.ComputerId == computer.Id && s.EndedAt == null, context.CancellationToken);

                if (activeComputerSession != null && activeComputerSession.UserId != user.Id)
                {
                    return new EnrollComputerForAuthUserResponse
                    {
                        Success = false,
                        Message = $"Computer {computer.Id} already has active session for another user"
                    };
                }

                UpdateComputerRuntimeFields(computer, hostname, request.OsVersion, request.IpAddress, normalizedMac, now);
            }

            var session = activeUserSession;
            var createdSession = false;
            if (session == null)
            {
                session = new ComputerSession
                {
                    UserId = user.Id,
                    AuthUserId = (int)request.AuthUserId,
                    ComputerId = computer.Id,
                    StartedAt = now,
                    ExpiresAt = expiresAt,
                    LastSeen = now,
                    Status = "active"
                };
                _db.ComputerSessions.Add(session);
                createdSession = true;
            }
            else
            {
                session.LastSeen = now;
                session.Status = "active";
                if (session.ExpiresAt == default || session.ExpiresAt > expiresAt)
                    session.ExpiresAt = expiresAt;
            }

            computer.UserId = user.Id;
            computer.Status = "active";
            computer.LastSeen = now;

            await _db.SaveChangesAsync(context.CancellationToken);
            await transaction.CommitAsync(context.CancellationToken);

            user.Computers = new List<Computer> { computer };

            return new EnrollComputerForAuthUserResponse
            {
                Success = true,
                Message = createdSession ? "Computer session started" : "Computer session refreshed",
                UserProfile = MapUserToProto(user),
                Computer = MapComputerToProto(computer),
                CreatedUser = createdUser,
                CreatedComputer = createdComputer,
                SessionId = session.Id,
                CreatedSession = createdSession,
                SessionExpiresAt = session.ExpiresAt.ToString("o")
            };
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Computer enrollment conflict for auth user ID: {AuthUserId}", request.AuthUserId);
            var resolvedConflict = await TryResolveEnrollmentConflictAsync(request, context.CancellationToken);
            if (resolvedConflict is not null)
                return resolvedConflict;

            return new EnrollComputerForAuthUserResponse
            {
                Success = false,
                Message = "Active session conflict: user or computer is already busy"
            };
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error while enrolling computer for auth user ID: {AuthUserId}", request.AuthUserId);
            return new EnrollComputerForAuthUserResponse
            {
                Success = false,
                Message = "Database error while enrolling computer"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enrolling computer for auth user ID: {AuthUserId}", request.AuthUserId);
            return new EnrollComputerForAuthUserResponse
            {
                Success = false,
                Message = "An error occurred while enrolling computer"
            };
        }
    }

    public override async Task<EndComputerSessionForAuthUserResponse> EndComputerSessionForAuthUser(EndComputerSessionForAuthUserRequest request, ServerCallContext context)
    {
        _logger.LogInformation("End computer session request for auth user ID: {AuthUserId}, session ID: {SessionId}", request.AuthUserId, request.SessionId);

        try
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.AuthUserId == request.AuthUserId, context.CancellationToken);
            if (user == null)
            {
                return new EndComputerSessionForAuthUserResponse
                {
                    Success = false,
                    Message = "User profile not found"
                };
            }

            var query = _db.ComputerSessions
                .Include(s => s.Computer)
                .Where(s => s.UserId == user.Id && s.EndedAt == null);

            if (request.SessionId > 0)
                query = query.Where(s => s.Id == request.SessionId);
            else if (request.ComputerId > 0)
                query = query.Where(s => s.ComputerId == request.ComputerId);

            var session = await query.FirstOrDefaultAsync(context.CancellationToken);
            if (session == null)
            {
                return new EndComputerSessionForAuthUserResponse
                {
                    Success = false,
                    Message = "Active session not found"
                };
            }

            var now = DateTime.UtcNow;
            session.EndedAt = now;
            session.LastSeen = now;
            session.Status = "ended";

            if (session.Computer != null && session.Computer.UserId == user.Id)
            {
                session.Computer.UserId = null;
                session.Computer.LastSeen = now;
                session.Computer.Status = "active";
            }

            await _db.SaveChangesAsync(context.CancellationToken);

            var response = new EndComputerSessionForAuthUserResponse
            {
                Success = true,
                Message = "Computer session ended",
                SessionId = session.Id
            };
            if (session.Computer != null)
                response.Computer = MapComputerToProto(session.Computer);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending computer session for auth user ID: {AuthUserId}", request.AuthUserId);
            return new EndComputerSessionForAuthUserResponse
            {
                Success = false,
                Message = "An error occurred while ending computer session"
            };
        }
    }

    public override async Task<DeleteUserResponse> DeleteUser(DeleteUserRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Delete user request for user ID: {UserId}", request.UserId);

        try
        {
            var user = await _db.Users
                .Include(u => u.Computers)
                .FirstOrDefaultAsync(u => u.Id == request.UserId);

            if (user == null)
            {
                return new DeleteUserResponse
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            // Remove computers if any
            if (user.Computers.Count > 0)
            {
                _db.Computers.RemoveRange(user.Computers);
            }

            // Remove user
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();

            return new DeleteUserResponse
            {
                Success = true,
                Message = "User deleted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user for ID: {UserId}", request.UserId);
            return new DeleteUserResponse
            {
                Success = false,
                Message = "An error occurred while deleting user"
            };
        }
    }

    private static UserProfile MapUserToProto(User user)
    {
        var userProfile = new UserProfile
        {
            Id = user.Id,
            AuthUserId = user.AuthUserId ?? 0,
            FullName = user.FullName ?? "",
            Department = user.Department ?? "",
            CreatedAt = user.CreatedAt.ToString("o")
        };

        var computers = user.Computers
            .OrderByDescending(c => c.UserId == user.Id)
            .ThenByDescending(c => c.LastSeen ?? c.CreatedAt)
            .ToList();

        if (computers.Count > 0)
        {
            foreach (var computer in computers)
                userProfile.Computers.Add(MapComputerToProto(computer));

            userProfile.Computer = userProfile.Computers[0];
        }

        return userProfile;
    }

    private static ComputerInfo MapComputerToProto(Computer computer)
    {
        return new ComputerInfo
        {
            Id = computer.Id,
            UserId = computer.UserId ?? 0,
            Hostname = computer.Hostname,
            OsVersion = computer.OsVersion ?? "",
            IpAddress = computer.IpAddress ?? "",
            MacAddress = computer.MacAddress ?? "",
            Status = computer.Status,
            LastSeen = computer.LastSeen?.ToString("o") ?? "",
            CreatedAt = computer.CreatedAt.ToString("o")
        };
    }

    private static string NormalizeRequired(string? value) => (value ?? string.Empty).Trim();

    private static string? NormalizeOptional(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void UpdateComputerRuntimeFields(
        Computer computer,
        string hostname,
        string? osVersion,
        string? ipAddress,
        string? macAddress,
        DateTime lastSeen)
    {
        if (!string.IsNullOrWhiteSpace(hostname))
            computer.Hostname = hostname;
        if (!string.IsNullOrWhiteSpace(osVersion))
            computer.OsVersion = osVersion.Trim();
        if (!string.IsNullOrWhiteSpace(ipAddress))
            computer.IpAddress = ipAddress.Trim();
        if (!string.IsNullOrWhiteSpace(macAddress))
            computer.MacAddress = macAddress;
        computer.LastSeen = lastSeen;
    }

    private async Task CloseExpiredSessionsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var expiredSessions = await _db.ComputerSessions
            .Include(s => s.Computer)
            .Where(s => s.EndedAt == null && s.ExpiresAt <= nowUtc)
            .ToListAsync(cancellationToken);

        if (expiredSessions.Count == 0)
            return;

        foreach (var session in expiredSessions)
        {
            var endedAt = session.ExpiresAt <= nowUtc ? session.ExpiresAt : nowUtc;
            session.EndedAt = endedAt;
            session.LastSeen = nowUtc;
            session.Status = "expired";

            if (session.Computer != null && session.Computer.UserId == session.UserId)
            {
                session.Computer.UserId = null;
                session.Computer.LastSeen = nowUtc;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<EnrollComputerForAuthUserResponse?> TryResolveEnrollmentConflictAsync(
        EnrollComputerForAuthUserRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentTransaction = _db.Database.CurrentTransaction;
            if (currentTransaction is not null)
                await currentTransaction.RollbackAsync(cancellationToken);
        }
        catch (Exception rollbackEx)
        {
            _logger.LogDebug(rollbackEx, "Failed to rollback failed enrollment transaction");
        }

        try
        {
            _db.ChangeTracker.Clear();

            var hostname = NormalizeRequired(request.Hostname);
            var normalizedMac = NormalizeOptional(request.MacAddress);
            var now = DateTime.UtcNow;
            var expiresAt = GetCurrentMoscowSessionExpiresAtUtc(now);

            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.AuthUserId == request.AuthUserId, cancellationToken);
            if (user is null)
                return null;

            Computer? computer = null;
            if (!string.IsNullOrWhiteSpace(normalizedMac))
            {
                computer = await _db.Computers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.MacAddress == normalizedMac, cancellationToken);
            }

            computer ??= await _db.Computers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Hostname == hostname, cancellationToken);

            var activeUserSession = await _db.ComputerSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == user.Id && s.EndedAt == null, cancellationToken);

            ComputerSession? activeComputerSession = null;
            if (computer is not null)
            {
                activeComputerSession = await _db.ComputerSessions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.ComputerId == computer.Id && s.EndedAt == null, cancellationToken);
            }

            if (computer is not null &&
                activeUserSession is not null &&
                activeUserSession.ComputerId == computer.Id &&
                (activeComputerSession is null || activeComputerSession.UserId == user.Id))
            {
                return await TryRefreshExistingEnrollmentAsync(
                    user.Id,
                    computer.Id,
                    activeUserSession.Id,
                    hostname,
                    request,
                    now,
                    expiresAt,
                    cancellationToken);
            }

            if (computer is not null &&
                activeComputerSession is not null &&
                activeComputerSession.UserId == user.Id)
            {
                return await TryRefreshExistingEnrollmentAsync(
                    user.Id,
                    computer.Id,
                    activeComputerSession.Id,
                    hostname,
                    request,
                    now,
                    expiresAt,
                    cancellationToken);
            }

            if (activeUserSession is not null)
                return EnrollmentConflictResponse($"User already has active session on computer {activeUserSession.ComputerId}");

            if (computer is not null && activeComputerSession is not null)
                return EnrollmentConflictResponse($"Computer {computer.Id} already has active session for another user");

            if (activeUserSession is null && (computer is not null || !string.IsNullOrWhiteSpace(hostname)))
            {
                return await TryStartRecoveredEnrollmentAsync(
                    user.Id,
                    (int)request.AuthUserId,
                    computer?.Id ?? 0,
                    hostname,
                    request,
                    now,
                    expiresAt,
                    cancellationToken);
            }
        }
        catch (Exception lookupEx)
        {
            _logger.LogDebug(lookupEx, "Failed to resolve enrollment conflict for auth user ID: {AuthUserId}", request.AuthUserId);
        }

        return null;
    }

    private async Task<EnrollComputerForAuthUserResponse?> TryRefreshExistingEnrollmentAsync(
        int userId,
        int computerId,
        long sessionId,
        string hostname,
        EnrollComputerForAuthUserRequest request,
        DateTime now,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .Include(u => u.Computers)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        var computer = await _db.Computers
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken);
        var session = await _db.ComputerSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.EndedAt == null, cancellationToken);

        if (user is null || computer is null || session is null)
            return null;

        UpdateComputerRuntimeFields(computer, hostname, request.OsVersion, request.IpAddress, NormalizeOptional(request.MacAddress), now);
        computer.UserId = user.Id;
        computer.Status = "active";
        computer.LastSeen = now;

        session.LastSeen = now;
        session.Status = "active";
        if (session.ExpiresAt == default || session.ExpiresAt > expiresAt)
            session.ExpiresAt = expiresAt;

        await _db.SaveChangesAsync(cancellationToken);

        user.Computers = new List<Computer> { computer };

        return new EnrollComputerForAuthUserResponse
        {
            Success = true,
            Message = "Computer session refreshed",
            UserProfile = MapUserToProto(user),
            Computer = MapComputerToProto(computer),
            CreatedUser = false,
            CreatedComputer = false,
            SessionId = session.Id,
            CreatedSession = false,
            SessionExpiresAt = session.ExpiresAt.ToString("o")
        };
    }

    private async Task<EnrollComputerForAuthUserResponse?> TryStartRecoveredEnrollmentAsync(
        int userId,
        int authUserId,
        int computerId,
        string hostname,
        EnrollComputerForAuthUserRequest request,
        DateTime now,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        try
        {
            _db.ChangeTracker.Clear();
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            var user = await _db.Users
                .Include(u => u.Computers)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
                return null;

            var activeUserSession = await _db.ComputerSessions
                .FirstOrDefaultAsync(s => s.UserId == user.Id && s.EndedAt == null, cancellationToken);
            if (activeUserSession is not null)
                return EnrollmentConflictResponse($"User already has active session on computer {activeUserSession.ComputerId}");

            Computer? computer = null;
            var createdComputer = false;
            if (computerId > 0)
            {
                computer = await _db.Computers
                    .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken);
            }

            if (computer is null)
            {
                if (string.IsNullOrWhiteSpace(hostname))
                    return null;

                computer = new Computer
                {
                    Hostname = hostname,
                    OsVersion = NormalizeOptional(request.OsVersion),
                    IpAddress = NormalizeOptional(request.IpAddress),
                    MacAddress = NormalizeOptional(request.MacAddress),
                    Status = "active",
                    LastSeen = now
                };
                _db.Computers.Add(computer);
                createdComputer = true;
                await _db.SaveChangesAsync(cancellationToken);
            }

            var activeComputerSession = await _db.ComputerSessions
                .FirstOrDefaultAsync(s => s.ComputerId == computer.Id && s.EndedAt == null, cancellationToken);
            if (activeComputerSession is not null && activeComputerSession.UserId != user.Id)
                return EnrollmentConflictResponse($"Computer {computer.Id} already has active session for another user");

            if (activeComputerSession is not null)
            {
                UpdateComputerRuntimeFields(computer, hostname, request.OsVersion, request.IpAddress, NormalizeOptional(request.MacAddress), now);
                computer.UserId = user.Id;
                computer.Status = "active";
                computer.LastSeen = now;

                activeComputerSession.LastSeen = now;
                activeComputerSession.Status = "active";
                if (activeComputerSession.ExpiresAt == default || activeComputerSession.ExpiresAt > expiresAt)
                    activeComputerSession.ExpiresAt = expiresAt;

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                user.Computers = new List<Computer> { computer };

                return new EnrollComputerForAuthUserResponse
                {
                    Success = true,
                    Message = "Computer session refreshed",
                    UserProfile = MapUserToProto(user),
                    Computer = MapComputerToProto(computer),
                    CreatedUser = false,
                    CreatedComputer = createdComputer,
                    SessionId = activeComputerSession.Id,
                    CreatedSession = false,
                    SessionExpiresAt = activeComputerSession.ExpiresAt.ToString("o")
                };
            }

            UpdateComputerRuntimeFields(computer, hostname, request.OsVersion, request.IpAddress, NormalizeOptional(request.MacAddress), now);
            computer.UserId = user.Id;
            computer.Status = "active";
            computer.LastSeen = now;

            var session = new ComputerSession
            {
                UserId = user.Id,
                AuthUserId = authUserId,
                ComputerId = computer.Id,
                StartedAt = now,
                ExpiresAt = expiresAt,
                LastSeen = now,
                Status = "active"
            };
            _db.ComputerSessions.Add(session);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            user.Computers = new List<Computer> { computer };

            return new EnrollComputerForAuthUserResponse
            {
                Success = true,
                Message = "Computer session started",
                UserProfile = MapUserToProto(user),
                Computer = MapComputerToProto(computer),
                CreatedUser = false,
                CreatedComputer = createdComputer,
                SessionId = session.Id,
                CreatedSession = true,
                SessionExpiresAt = session.ExpiresAt.ToString("o")
            };
        }
        catch (DbUpdateException ex)
        {
            _logger.LogDebug(ex, "Recovered enrollment start failed for auth user ID: {AuthUserId}", request.AuthUserId);
            return null;
        }
    }

    private static EnrollComputerForAuthUserResponse EnrollmentConflictResponse(string message)
    {
        return new EnrollComputerForAuthUserResponse
        {
            Success = false,
            Message = message
        };
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        HasPostgresSqlState(ex, "23505");

    private static bool HasPostgresSqlState(Exception? ex, string sqlState)
    {
        while (ex is not null)
        {
            if (ex is PostgresException postgresException && postgresException.SqlState == sqlState)
                return true;

            ex = ex.InnerException;
        }

        return false;
    }

    private static DateTime GetCurrentMoscowSessionExpiresAtUtc(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);

        var moscowTz = TryGetMoscowTimeZone();
        if (moscowTz is not null)
        {
            var moscowNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, moscowTz);
            var moscowExpiry = new DateTime(
                moscowNow.Year,
                moscowNow.Month,
                moscowNow.Day,
                23,
                0,
                0,
                DateTimeKind.Unspecified);

            if (moscowNow >= moscowExpiry)
                moscowExpiry = moscowExpiry.AddDays(1);

            return TimeZoneInfo.ConvertTimeToUtc(moscowExpiry, moscowTz);
        }

        var moscowNowFallback = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToOffset(MoscowUtcOffset);
        var moscowExpiryFallback = new DateTimeOffset(
            moscowNowFallback.Year,
            moscowNowFallback.Month,
            moscowNowFallback.Day,
            23,
            0,
            0,
            MoscowUtcOffset);

        if (moscowNowFallback >= moscowExpiryFallback)
            moscowExpiryFallback = moscowExpiryFallback.AddDays(1);

        return moscowExpiryFallback.UtcDateTime;
    }

    private static TimeZoneInfo? TryGetMoscowTimeZone()
    {
        foreach (var timeZoneId in new[] { "Europe/Moscow", "Russian Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return null;
    }
}
