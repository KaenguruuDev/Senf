using Microsoft.AspNetCore.Mvc;
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
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        app.MapPost("/keys", CreateSshKey)
            .WithName("CreateSshKey")
            .Produces<SshKeyResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .RequireAuthorization();

        app.MapDelete("/keys", DeleteSshKey)
            .WithName("DeleteSshKey")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .RequireAuthorization();
    }

    private static async Task<IResult> GetSshKeys(int? keyId, HttpContext context,
        IUserManagementService userManagementService)
    {
        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        if (keyId.HasValue)
        {
            var (success, error, key) = await userManagementService.GetSshKeyAsync(userId, keyId.Value);
            if (success)
                return Results.Ok(key);

            if (error == null)
                return ApiProblem.Unexpected(context);

            return ApiProblem.FromError(error, context);
        }

        var (listSuccess, listError, keys) = await userManagementService.GetUserSshKeysAsync(userId);
        return listSuccess
            ? Results.Ok(new SshKeysListResponse { Keys = keys! })
            : listError == null
                ? ApiProblem.Unexpected(context)
                : ApiProblem.FromError(listError, context);
    }

    private static async Task<IResult> CreateSshKey(HttpContext context,
        IUserManagementService userManagementService, SshKeyCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PublicKey))
            return ApiProblem.Validation(SshKeyErrors.PublicKeyRequired, context, "publicKey");

        if (string.IsNullOrWhiteSpace(request.Name))
            return ApiProblem.Validation(SshKeyErrors.NameRequired, context, "name");

        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        var (success, error, keyResponse) = await userManagementService.AddSshKeyAsync(userId, request.PublicKey, request.Name);
        return success
            ? Results.Created($"/keys/{keyResponse?.Id}", keyResponse)
            : error == null
                ? ApiProblem.Unexpected(context)
                : ApiProblem.FromError(error, context);
    }

    private static async Task<IResult> DeleteSshKey(int? keyId, HttpContext context,
        IUserManagementService userManagementService)
    {
        if (!keyId.HasValue)
            return ApiProblem.Validation(SshKeyErrors.KeyIdRequired, context, "keyId");

        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        var (success, error) = await userManagementService.DeleteSshKeyAsync(userId, keyId.Value);
        if (success)
            return Results.Ok();

        if (error == null)
            return ApiProblem.Unexpected(context);

        return ApiProblem.FromError(error, context);
    }
}
