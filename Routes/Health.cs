using Senf.Data;

namespace Senf.Routes;

public static class HealthRoutes
{
    public static void MapHealthRoutes(this WebApplication app)
    {
        app.MapGet("/healthz", GetHealth)
            .WithName("GetHealth")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> GetHealth(AppDbContext dbContext)
    {
        var canConnect = await dbContext.Database.CanConnectAsync();
        if (!canConnect)
        {
            return Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new { status = "healthy" });
    }
}
