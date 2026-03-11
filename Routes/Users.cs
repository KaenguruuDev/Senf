using Senf.Dtos;
using Senf.Services;

namespace Senf.Routes;

public static class UsersRoutes
{
    public static void MapUserRoutes(this WebApplication app)
    {
        app.MapGet("/users", GetUsers)
            .WithName("GetUsers")
            .Produces<UsersListResponse>(StatusCodes.Status200OK)
            .RequireAuthorization();
    }

    private static async Task<IResult> GetUsers(IUserManagementService userManagementService)
    {
        var users = await userManagementService.GetUsersAsync();
        return Results.Ok(new UsersListResponse { Users = users });
    }
}
