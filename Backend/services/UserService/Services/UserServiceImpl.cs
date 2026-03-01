using Grpc.Core;
using UserService.Data;
using UserService.Models;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;

namespace UserService.Services;

public class UserServiceImpl : UserService.UserServiceBase
{
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
                .Include(u => u.Computer)
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
                .Include(u => u.Computer)
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
                .Include(u => u.Computer)
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
            
            var query = _db.Users.Include(u => u.Computer).AsQueryable();
            
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
            if (string.IsNullOrWhiteSpace(hostname))
            {
                return new CreateUserResponse
                {
                    Success = false,
                    Message = "Hostname is required (1:1 user-computer policy)"
                };
            }

            var normalizedMac = string.IsNullOrWhiteSpace(request.MacAddress) ? string.Empty : request.MacAddress.Trim();

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

            if (await _db.Computers.AnyAsync(c => c.Hostname == hostname))
            {
                return new CreateUserResponse
                {
                    Success = false,
                    Message = "Computer hostname is already assigned to another user"
                };
            }

            if (!string.IsNullOrWhiteSpace(normalizedMac) && await _db.Computers.AnyAsync(c => c.MacAddress == normalizedMac))
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

            var computer = new Computer
            {
                UserId = user.Id,
                Hostname = hostname,
                OsVersion = request.OsVersion,
                IpAddress = request.IpAddress,
                MacAddress = normalizedMac,
                Status = "active",
                LastSeen = DateTime.UtcNow
            };

            _db.Computers.Add(computer);
            await _db.SaveChangesAsync(context.CancellationToken);
            await transaction.CommitAsync(context.CancellationToken);

            user.Computer = computer;

            return new CreateUserResponse
            {
                Success = true,
                Message = "User created successfully",
                UserProfile = MapUserToProto(user),
                Computer = MapComputerToProto(computer)
            };
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

    public override async Task<DeleteUserResponse> DeleteUser(DeleteUserRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Delete user request for user ID: {UserId}", request.UserId);

        try
        {
            var user = await _db.Users
                .Include(u => u.Computer)
                .FirstOrDefaultAsync(u => u.Id == request.UserId);

            if (user == null)
            {
                return new DeleteUserResponse
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            // Remove computer if exists
            if (user.Computer != null)
            {
                _db.Computers.Remove(user.Computer);
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

        if (user.Computer != null)
        {
            userProfile.Computer = MapComputerToProto(user.Computer);
        }

        return userProfile;
    }

    private static ComputerInfo MapComputerToProto(Computer computer)
    {
        return new ComputerInfo
        {
            Id = computer.Id,
            UserId = computer.UserId,
            Hostname = computer.Hostname,
            OsVersion = computer.OsVersion ?? "",
            IpAddress = computer.IpAddress ?? "",
            MacAddress = computer.MacAddress ?? "",
            Status = computer.Status,
            LastSeen = computer.LastSeen?.ToString("o") ?? "",
            CreatedAt = computer.CreatedAt.ToString("o")
        };
    }
}
