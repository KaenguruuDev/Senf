using Senf.Dtos;
using Senf.Services;

namespace Senf.Routes;

public static class EnvFilesRoutes
{
    public static void MapEnvFileRoutes(this WebApplication app)
    {
        app.MapGet("/env", GetEnvFile)
            .WithName("GetEnvFile")
            .Produces<EnvFileResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        app.MapPatch("/env", UpdateEnvFile)
            .WithName("UpdateEnvFile")
            .Produces(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        app.MapPut("/env", CreateOrReplaceEnvFile)
            .WithName("CreateOrReplaceEnvFile")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        app.MapDelete("/env", DeleteEnvFile)
            .WithName("DeleteEnvFile")
            .Produces(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private static async Task<IResult> GetEnvFile(string? name, HttpContext context, IEnvFileService envFileService)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Results.BadRequest(new ErrorResponse { Error = "Environment name is required" });

        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        var (success, message, file) = await envFileService.GetEnvFileAsync(userId, name);
        return success ? Results.Ok(file) : Results.NotFound(new ErrorResponse { Error = message });
    }

    private static async Task<IResult> UpdateEnvFile(string? name, HttpContext context,
        IEnvFileService envFileService, EnvFileUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Results.BadRequest(new ErrorResponse { Error = "Environment name is required" });

        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
        var sshKeyId = int.Parse(context.User.FindFirst("SshKeyId")?.Value ?? "0");

        var (success, message) = await envFileService.UpdateEnvFileAsync(userId, name, request.Content, sshKeyId);
        return success
            ? Results.Ok()
            : Results.NotFound(new ErrorResponse { Error = message });
    }

    private static async Task<IResult> CreateOrReplaceEnvFile(string? name, HttpContext context,
        IEnvFileService envFileService, EnvFileUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Results.BadRequest(new ErrorResponse { Error = "Environment name is required" });

        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
        var sshKeyId = int.Parse(context.User.FindFirst("SshKeyId")?.Value ?? "0");

        var (exists, _, _) = await envFileService.GetEnvFileAsync(userId, name);

        if (exists)
        {
            var (success, message) = await envFileService.UpdateEnvFileAsync(userId, name, request.Content, sshKeyId);
            return success ? Results.Ok() : Results.BadRequest(new ErrorResponse { Error = message });
        }
        else
        {
            var (success, message) = await envFileService.CreateEnvFileAsync(userId, name, request.Content, sshKeyId);
            return success
                ? Results.Created($"/env?name={Uri.EscapeDataString(name)}", null)
                : Results.BadRequest(new ErrorResponse { Error = message });
        }
    }

    private static async Task<IResult> DeleteEnvFile(string? name, HttpContext context, IEnvFileService envFileService)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Results.BadRequest(new ErrorResponse { Error = "Environment name is required" });

        var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        var (success, message) = await envFileService.DeleteEnvFileAsync(userId, name);
        return success
            ? Results.Ok()
            : Results.NotFound(new ErrorResponse { Error = message });
    }
}
