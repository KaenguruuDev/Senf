using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Senf.Data;
using Senf.Dtos;
using Senf.Models;
using Senf.Services;

namespace Senf.Routes;

public static class SharingRoutes
{
	public static void MapSharingRoutes(this WebApplication app)
	{
		app.MapPost("/share", ShareEnvFileAsync)
			.WithName("ShareEnvFile")
			.Produces<ShareResponse>()
			.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
			.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
			.Produces<ProblemDetails>(StatusCodes.Status409Conflict)
			.RequireAuthorization();

		app.MapDelete("/share", RemoveShareAsync)
			.WithName("RemoveShare")
			.Produces(StatusCodes.Status200OK)
			.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
			.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
			.RequireAuthorization();

		app.MapGet("/shares", GetActiveSharesAsync)
			.WithName("GetActiveShares")
			.Produces<SharesListResponse>()
			.RequireAuthorization();

		app.MapGet("/shared", GetSharedFilesAsync)
			.WithName("GetSharedFiles")
			.Produces<SharedEnvFilesListResponse>()
			.RequireAuthorization();

		app.MapGet("/shared/{shareId:int}", GetSharedFileAsync)
			.WithName("GetSharedFile")
			.Produces<SharedEnvFileResponse>()
			.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
			.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
			.RequireAuthorization();

		app.MapPatch("/shared/{shareId:int}", UpdateSharedFileAsync)
			.WithName("UpdateSharedFile")
			.Produces(StatusCodes.Status200OK)
			.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
			.Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
			.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
			.RequireAuthorization();
	}

	private static async Task<IResult> GetActiveSharesAsync(HttpContext context, AppDbContext db)
	{
		var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

		var shares = await db.Shares
			.AsNoTracking()
			.Include(share => share.EnvFile)
			.Include(share => share.SharedToUser)
			.Where(share => share.EnvFile.UserId == userId)
			.OrderByDescending(share => share.UpdatedAt)
			.ToListAsync();

		var response = new SharesListResponse
		{
			Shares = shares.Select(share => new ShareResponse
			{
				Id = share.Id,
				EnvFileId = share.EnvFileId,
				EnvFileName = share.EnvFile.Name,
				SharedToUserId = share.SharedToUserId,
				SharedToUsername = share.SharedToUser.Username,
				ShareMode = ToDtoShareMode(share.ShareMode),
				CreatedAt = share.CreatedAt,
				UpdatedAt = share.UpdatedAt
			}).ToList()
		};

		return Results.Ok(response);
	}

	private static async Task<IResult> GetSharedFilesAsync(HttpContext context, AppDbContext db,
		IEncryptionService encryptionService)
	{
		var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

		var shares = await db.Shares
			.AsNoTracking()
			.Include(share => share.EnvFile)
			.ThenInclude(envFile => envFile.User)
			.Where(share => share.SharedToUserId == userId)
			.OrderByDescending(share => share.UpdatedAt)
			.ToListAsync();

		var response = new SharedEnvFilesListResponse
		{
			Files = shares.Select(share => new SharedEnvFileResponse
			{
				ShareId = share.Id,
				EnvFileId = share.EnvFileId,
				EnvFileName = share.EnvFile.Name,
				OwnerUserId = share.EnvFile.UserId,
				OwnerUsername = share.EnvFile.User.Username,
				Content = encryptionService.Decrypt(share.EnvFile.EncryptedContent),
				LastUpdatedByKeyId = share.EnvFile.LastUpdatedBySshKeyId,
				ShareMode = ToDtoShareMode(share.ShareMode),
				SharedAt = share.CreatedAt,
				UpdatedAt = share.UpdatedAt
			}).ToList()
		};

		return Results.Ok(response);
	}

	private static async Task<IResult> GetSharedFileAsync(int shareId, HttpContext context, AppDbContext db,
		IEncryptionService encryptionService)
	{
		if (shareId <= 0)
			return ApiProblem.Validation(SharingErrors.ShareIdRequired, context, "shareId");

		var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

		var share = await db.Shares
			.AsNoTracking()
			.Include(existingShare => existingShare.EnvFile)
			.ThenInclude(envFile => envFile.User)
			.FirstOrDefaultAsync(existingShare => existingShare.Id == shareId && existingShare.SharedToUserId == userId);

		if (share == null)
			return ApiProblem.FromError(SharingErrors.ShareNotFound, context);

		var response = new SharedEnvFileResponse
		{
			ShareId = share.Id,
			EnvFileId = share.EnvFileId,
			EnvFileName = share.EnvFile.Name,
			OwnerUserId = share.EnvFile.UserId,
			OwnerUsername = share.EnvFile.User.Username,
			Content = encryptionService.Decrypt(share.EnvFile.EncryptedContent),
			LastUpdatedByKeyId = share.EnvFile.LastUpdatedBySshKeyId,
			ShareMode = ToDtoShareMode(share.ShareMode),
			SharedAt = share.CreatedAt,
			UpdatedAt = share.UpdatedAt
		};

		return Results.Ok(response);
	}

	private static async Task<IResult> UpdateSharedFileAsync(int shareId, HttpContext context, AppDbContext db,
		IEncryptionService encryptionService, EnvFileUpdateRequest request)
	{
		if (shareId <= 0)
			return ApiProblem.Validation(SharingErrors.ShareIdRequired, context, "shareId");

		if (string.IsNullOrWhiteSpace(request.Content))
			return ApiProblem.Validation(SharingErrors.ContentRequired, context, "content");

		var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
		var sshKeyId = int.Parse(context.User.FindFirst("SshKeyId")?.Value ?? "0");

		var share = await db.Shares
			.Include(existingShare => existingShare.EnvFile)
			.FirstOrDefaultAsync(existingShare => existingShare.Id == shareId && existingShare.SharedToUserId == userId);

		if (share == null)
			return ApiProblem.FromError(SharingErrors.ShareNotFound, context);

		if (share.ShareMode != ShareMode.ReadWrite)
			return ApiProblem.FromError(SharingErrors.ReadOnly, context);

		share.EnvFile.EncryptedContent = encryptionService.Encrypt(request.Content);
		share.EnvFile.UpdatedAt = DateTime.UtcNow;
		share.EnvFile.LastUpdatedBySshKeyId = sshKeyId;
		share.UpdatedAt = DateTime.UtcNow;

		await db.SaveChangesAsync();

		return Results.Ok();
	}

	private static async Task<IResult> ShareEnvFileAsync(HttpContext context, AppDbContext db,
		ShareEnvFileRequest request)
	{
		if (request.EnvFileId <= 0)
			return ApiProblem.Validation(SharingErrors.EnvFileIdRequired, context, "envFileId");

		if (request.ShareToUserId <= 0)
			return ApiProblem.Validation(SharingErrors.ShareToUserIdRequired, context, "shareToUserId");

		if (!Enum.IsDefined(typeof(ShareModeDto), request.ShareMode))
			return ApiProblem.Validation(SharingErrors.ShareModeInvalid, context, "shareMode");

		var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
		if (request.ShareToUserId == userId)
			return ApiProblem.FromError(SharingErrors.CannotShareToSelf, context);

		var envFile = await db.EnvFiles
			.AsNoTracking()
			.FirstOrDefaultAsync(file => file.Id == request.EnvFileId && file.UserId == userId);

		if (envFile == null)
			return ApiProblem.FromError(SharingErrors.EnvFileNotFound, context);

		var sharedToUser = await db.Users
			.AsNoTracking()
			.FirstOrDefaultAsync(user => user.Id == request.ShareToUserId);

		if (sharedToUser == null)
			return ApiProblem.FromError(SharingErrors.ShareToUserNotFound, context);

		var share = await db.Shares
			.FirstOrDefaultAsync(existing =>
				existing.EnvFileId == request.EnvFileId && existing.SharedToUserId == request.ShareToUserId);

		if (share == null)
		{
			share = new Share
			{
				EnvFileId = request.EnvFileId,
				SharedToUserId = request.ShareToUserId,
				ShareMode = ToModelShareMode(request.ShareMode),
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			};

			db.Shares.Add(share);
		}
		else
		{
			share.ShareMode = ToModelShareMode(request.ShareMode);
			share.UpdatedAt = DateTime.UtcNow;
		}

		try
		{
			await db.SaveChangesAsync();
		}
		catch (DbUpdateException)
		{
			var existingShare = await db.Shares
				.AsNoTracking()
				.FirstOrDefaultAsync(existing =>
					existing.EnvFileId == request.EnvFileId &&
					existing.SharedToUserId == request.ShareToUserId);

			return existingShare == null
				? ApiProblem.Unexpected(context)
				: ApiProblem.FromError(SharingErrors.Conflict, context);
		}

		var response = new ShareResponse
		{
			Id = share.Id,
			EnvFileId = envFile.Id,
			EnvFileName = envFile.Name,
			SharedToUserId = sharedToUser.Id,
			SharedToUsername = sharedToUser.Username,
			ShareMode = ToDtoShareMode(share.ShareMode),
			CreatedAt = share.CreatedAt,
			UpdatedAt = share.UpdatedAt
		};

		return Results.Ok(response);
	}

	private static async Task<IResult> RemoveShareAsync(HttpContext context, AppDbContext db,
		[FromBody] RemoveEnvShareRequest request)
	{
		if (request.EnvFileId <= 0)
			return ApiProblem.Validation(SharingErrors.EnvFileIdRequired, context, "envFileId");

		if (request.ShareToUserId.HasValue && request.ShareToUserId <= 0)
			return ApiProblem.Validation(SharingErrors.ShareToUserIdInvalid, context, "shareToUserId");

		var userId = int.Parse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
		var envFileExists = await db.EnvFiles
			.AsNoTracking()
			.AnyAsync(file => file.Id == request.EnvFileId && file.UserId == userId);

		if (!envFileExists)
			return ApiProblem.FromError(SharingErrors.EnvFileNotFound, context);

		var shareQuery = db.Shares
			.Where(share => share.EnvFileId == request.EnvFileId);

		if (request.ShareToUserId.HasValue)
			shareQuery = shareQuery.Where(share => share.SharedToUserId == request.ShareToUserId.Value);

		var shares = await shareQuery.ToListAsync();
		if (shares.Count == 0)
			return ApiProblem.FromError(SharingErrors.ShareNotFound, context);

		db.Shares.RemoveRange(shares);
		await db.SaveChangesAsync();

		return Results.Ok();
	}

	private static ShareModeDto ToDtoShareMode(ShareMode shareMode)
		=> shareMode switch
		{
			ShareMode.ReadWrite => ShareModeDto.ReadWrite,
			_ => ShareModeDto.ReadOnly
		};

	private static ShareMode ToModelShareMode(ShareModeDto shareMode)
		=> shareMode switch
		{
			ShareModeDto.ReadWrite => ShareMode.ReadWrite,
			_ => ShareMode.ReadOnly
		};
}