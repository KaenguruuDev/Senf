using System.Security.Claims;

namespace Senf.Services;

public interface IAdminAuthorizationService
{
    bool IsAdmin(ClaimsPrincipal user);
    bool IsAdmin(string? username, int? userId);
}

public class AdminAuthorizationService(IAdminConfigProvider configProvider) : IAdminAuthorizationService
{
    private readonly IAdminConfigProvider _configProvider = configProvider;

    public bool IsAdmin(ClaimsPrincipal user)
    {
        var username = user.FindFirst(ClaimTypes.Name)?.Value;
        var userIdValue = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userId = int.TryParse(userIdValue, out var parsedUserId)
            ? parsedUserId
            : (int?)null;

        return IsAdmin(username, userId);
    }

    public bool IsAdmin(string? username, int? userId)
    {
        var config = _configProvider.GetConfig();

        if (userId.HasValue && config.Admins.Any(a => a.UserId == userId.Value))
            return true;

        if (string.IsNullOrWhiteSpace(username))
            return false;

        return config.Admins.Any(a => string.Equals(a.Username, username, StringComparison.Ordinal));
    }
}
