using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Senf.Data;
using Senf.Dtos;
using Senf.Services;

namespace Senf.Routes;

public static class InviteRoutes
{
    private static readonly TimeSpan InviteTtl = TimeSpan.FromHours(24);

    public static void MapInviteRoutes(this WebApplication app)
    {
        app.MapGet("/invites", ListInvites)
            .WithName("ListInvites")
            .Produces<InviteListResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        app.MapPost("/invites", CreateInvite)
            .WithName("CreateInvite")
            .Produces<InviteCreateResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        app.MapDelete("/invites/{token}", DeleteInvite)
            .WithName("DeleteInvite")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        app.MapPost("/join", Join)
            .WithName("JoinWithInvite")
            .Produces<JoinResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListInvites(HttpContext context,
        IInviteService inviteService,
        IAdminAuthorizationService adminAuthorizationService)
    {
        if (!adminAuthorizationService.IsAdmin(context.User))
            return ApiProblem.FromError(UserErrors.AdminRequired, context);

        var invites = await inviteService.GetInvitesAsync();
        return Results.Ok(new InviteListResponse { Invites = invites });
    }

    private static async Task<IResult> CreateInvite(HttpContext context,
        IInviteService inviteService,
        IAdminAuthorizationService adminAuthorizationService)
    {
        if (!adminAuthorizationService.IsAdmin(context.User))
            return ApiProblem.FromError(UserErrors.AdminRequired, context);

        var userId = int.Parse(context.User
            .FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        var invite = await inviteService.CreateInviteAsync(userId, InviteTtl);
        var joinUrl = $"/join?token={invite.Token}";

        return Results.Created("/invites", new InviteCreateResponse
        {
            Token = invite.Token,
            RelativeJoinUrl = joinUrl,
            ExpiresAt = invite.ExpiresAt
        });
    }

    private static async Task<IResult> DeleteInvite(string token, HttpContext context,
        IInviteService inviteService,
        IAdminAuthorizationService adminAuthorizationService)
    {
        if (!adminAuthorizationService.IsAdmin(context.User))
            return ApiProblem.FromError(UserErrors.AdminRequired, context);

        var (success, error) = await inviteService.DeleteInviteAsync(token);
        if (success)
            return Results.Ok();

        return error == null
            ? ApiProblem.Unexpected(context)
            : ApiProblem.FromError(error, context);
    }

    private static async Task<IResult> Join(HttpContext context,
        JoinRequest request,
        IInviteService inviteService,
        IUserManagementService userManagementService,
        AppDbContext dbContext)
    {
        var token = request.Token;
        if (string.IsNullOrWhiteSpace(token))
            token = context.Request.Query["token"].ToString();

        if (string.IsNullOrWhiteSpace(token))
            return ApiProblem.Validation(InviteErrors.TokenRequired, context, "token");

        if (string.IsNullOrWhiteSpace(request.Username))
            return ApiProblem.Validation(UserErrors.UsernameRequired, context, "username");

        if (request.PublicKeys == null || request.PublicKeys.Count == 0)
            return ApiProblem.Validation(UserErrors.PublicKeysRequired, context, "publicKeys");

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var (invite, inviteError) = await inviteService.TryGetValidInviteAsync(token);
        if (inviteError != null)
            return ApiProblem.FromError(inviteError, context);

        var (success, userError, user) = await userManagementService
            .CreateUserWithKeysAsync(request.Username, request.PublicKeys);

        if (!success)
        {
            if (userError == null)
                return ApiProblem.Unexpected(context);

            return ApiProblem.FromError(userError, context);
        }

        invite!.UsedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return Results.Created($"/users/{user!.Id}", new JoinResponse
        {
            UserId = user.Id,
            Username = user.Username
        });
    }
}
