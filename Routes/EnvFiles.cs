using Microsoft.AspNetCore.Mvc;
using Senf.Dtos;
using Senf.Services;

namespace Senf.Routes;

public static class EnvFilesRoutes
{
    public static void MapEnvFileRoutes(this WebApplication app)
    {
        app.MapGet("/env", GetEnvFile)
            .WithName("GetEnvFile")
            .Produces<EnvFileResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        app.MapPatch("/env", UpdateEnvFile)
            .WithName("UpdateEnvFile")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        app.MapPut("/env", CreateOrReplaceEnvFile)
            .WithName("CreateOrReplaceEnvFile")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .RequireAuthorization();

        app.MapDelete("/env", DeleteEnvFile)
            .WithName("DeleteEnvFile")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private static async Task<IResult> GetEnvFile(string? name, HttpContext context, IEnvFileService envFileService)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ApiProblem.Validation(EnvFileErrors.NameRequired, context, "name");

        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        var (success, error, file) = await envFileService.GetEnvFileAsync(userId, name);
        if (success)
            return Results.Ok(file);

        if (error == null)
            return ApiProblem.Unexpected(context);

        return ApiProblem.FromError(error, context);
    }

    private static async Task<IResult> UpdateEnvFile(string? name, HttpContext context,
        IEnvFileService envFileService, EnvFileUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ApiProblem.Validation(EnvFileErrors.NameRequired, context, "name");

        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
        var sshKeyId = int.Parse(context.User.FindFirst("SshKeyId")?.Value ?? "0");

        var (success, error) = await envFileService.UpdateEnvFileAsync(userId, name, request.Content, sshKeyId);
        if (success)
            return Results.Ok();

        if (error == null)
            return ApiProblem.Unexpected(context);

        return error.Code == EnvFileErrors.ContentRequiredCode
            ? ApiProblem.Validation(error, context, "content")
            : ApiProblem.FromError(error, context);
    }

    private static async Task<IResult> CreateOrReplaceEnvFile(string? name, HttpContext context,
        IEnvFileService envFileService, EnvFileUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ApiProblem.Validation(EnvFileErrors.NameRequired, context, "name");

        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
        var sshKeyId = int.Parse(context.User.FindFirst("SshKeyId")?.Value ?? "0");

        var exists = await envFileService.ExistsEnvFileAsync(userId, name);

        if (exists)
        {
            var (success, error) = await envFileService.UpdateEnvFileAsync(userId, name, request.Content, sshKeyId);
            return success
                ? Results.Ok()
                : error == null
                    ? ApiProblem.Unexpected(context)
                    : error.Code == EnvFileErrors.ContentRequiredCode
                        ? ApiProblem.Validation(error, context, "content")
                        : ApiProblem.FromError(error, context);
        }
        else
        {
            var (success, error) = await envFileService.CreateEnvFileAsync(userId, name, request.Content, sshKeyId);
            return success
                ? Results.Created($"/env?name={Uri.EscapeDataString(name)}", null)
                : error == null
                    ? ApiProblem.Unexpected(context)
                    : error.Code == EnvFileErrors.ContentRequiredCode
                        ? ApiProblem.Validation(error, context, "content")
                        : ApiProblem.FromError(error, context);
        }
    }

    private static async Task<IResult> DeleteEnvFile(string? name, HttpContext context, IEnvFileService envFileService)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ApiProblem.Validation(EnvFileErrors.NameRequired, context, "name");

        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        var (success, error) = await envFileService.DeleteEnvFileAsync(userId, name);
        if (success)
            return Results.Ok();

        if (error == null)
            return ApiProblem.Unexpected(context);

        return ApiProblem.FromError(error, context);
    }
}
