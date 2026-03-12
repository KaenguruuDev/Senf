using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Senf.Data;
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

        app.MapDelete("/users/{userId:int}", DeleteUser)
            .WithName("DeleteUser")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private static async Task<IResult> GetUsers(IUserManagementService userManagementService)
    {
        var users = await userManagementService.GetUsersAsync();
        return Results.Ok(new UsersListResponse { Users = users });
    }

    private static async Task<IResult> DeleteUser(int userId, HttpContext context,
        IUserManagementService userManagementService,
        IAdminAuthorizationService adminAuthorizationService,
        AppDbContext dbContext)
    {
        if (!adminAuthorizationService.IsAdmin(context.User))
            return ApiProblem.FromError(UserErrors.AdminRequired, context);

        var currentUserId = int.Parse(
            context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        if (currentUserId == userId)
            return ApiProblem.FromError(UserErrors.CannotDeleteSelf, context);

        var targetUser = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (targetUser == null)
            return ApiProblem.FromError(UserErrors.NotFound, context);

        if (adminAuthorizationService.IsAdmin(targetUser.Username, targetUser.Id))
            return ApiProblem.FromError(UserErrors.CannotDeleteAdmin, context);

        var (success, error) = await userManagementService.DeleteUserAsync(userId);
        if (success)
            return Results.Ok();

        return error == null
            ? ApiProblem.Unexpected(context)
            : ApiProblem.FromError(error, context);
    }
}
