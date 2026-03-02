using Senf.Dtos;
using Senf.Services;

namespace Senf.Routes;

public static class SshKeysRoutes
{
    public static void MapSshKeyRoutes(this WebApplication app)
    {
        app.MapGet("/keys", GetSshKeys)
            .WithName("GetSshKeys")
            .Produces<SshKeyResponse>(StatusCodes.Status200OK)
            .Produces<SshKeysListResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        app.MapPost("/keys", CreateSshKey)
            .WithName("CreateSshKey")
            .Produces<SshKeyResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        app.MapDelete("/keys", DeleteSshKey)
            .WithName("DeleteSshKey")
            .Produces(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private static async Task<IResult> GetSshKeys(int? keyId, HttpContext context,
        IUserManagementService userManagementService)
    {
        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        if (keyId.HasValue)
        {
            var (success, message, key) = await userManagementService.GetSshKeyAsync(userId, keyId.Value);
            return success ? Results.Ok(key) : Results.NotFound(new ErrorResponse { Error = message });
        }

        var (listSuccess, listMessage, keys) = await userManagementService.GetUserSshKeysAsync(userId);
        return listSuccess
            ? Results.Ok(new SshKeysListResponse { Keys = keys! })
            : Results.BadRequest(new ErrorResponse { Error = listMessage });
    }

    private static async Task<IResult> CreateSshKey(HttpContext context,
        IUserManagementService userManagementService, SshKeyCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PublicKey))
            return Results.BadRequest(new ErrorResponse { Error = "Public key is required" });

        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new ErrorResponse { Error = "Key name is required" });

        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        var (success, message, keyResponse) = await userManagementService.AddSshKeyAsync(userId, request.PublicKey, request.Name);
        return success
            ? Results.Created($"/keys/{keyResponse?.Id}", keyResponse)
            : Results.BadRequest(new ErrorResponse { Error = message });
    }

    private static async Task<IResult> DeleteSshKey(int? keyId, HttpContext context,
        IUserManagementService userManagementService)
    {
        if (!keyId.HasValue)
            return Results.BadRequest(new ErrorResponse { Error = "Key ID is required" });

        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        var (success, message) = await userManagementService.DeleteSshKeyAsync(userId, keyId.Value);
        return success
            ? Results.Ok()
            : Results.NotFound(new ErrorResponse { Error = message });
    }
}
