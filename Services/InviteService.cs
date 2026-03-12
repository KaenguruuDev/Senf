using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Senf.Data;
using Senf.Dtos;
using Senf.Models;

namespace Senf.Services;

public interface IInviteService
{
    Task<InviteToken> CreateInviteAsync(int createdByUserId, TimeSpan ttl);
    Task<(InviteToken? Invite, InviteError? Error)> TryGetValidInviteAsync(string token);
    Task<List<InviteSummaryResponse>> GetInvitesAsync();
    Task<(bool Success, InviteError? Error)> DeleteInviteAsync(string token);
}

public class InviteService(AppDbContext dbContext) : IInviteService
{
    private readonly AppDbContext _dbContext = dbContext;

    public async Task<InviteToken> CreateInviteAsync(int createdByUserId, TimeSpan ttl)
    {
        var token = GenerateToken();
        var now = DateTime.UtcNow;

        var invite = new InviteToken
        {
            Token = token,
            CreatedAt = now,
            ExpiresAt = now.Add(ttl),
            CreatedByUserId = createdByUserId
        };

        _dbContext.InviteTokens.Add(invite);
        await _dbContext.SaveChangesAsync();

        return invite;
    }

    public async Task<(InviteToken? Invite, InviteError? Error)> TryGetValidInviteAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, InviteErrors.TokenRequired);

        var invite = await _dbContext.InviteTokens
            .FirstOrDefaultAsync(i => i.Token == token);

        if (invite == null)
            return (null, InviteErrors.TokenInvalid);

        if (invite.UsedAt.HasValue)
            return (null, InviteErrors.TokenUsed);

        if (invite.ExpiresAt <= DateTime.UtcNow)
            return (null, InviteErrors.TokenExpired);

        return (invite, null);
    }

    public async Task<List<InviteSummaryResponse>> GetInvitesAsync()
    {
        return await _dbContext.InviteTokens
            .AsNoTracking()
            .Include(i => i.CreatedByUser)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InviteSummaryResponse
            {
                Token = i.Token,
                CreatedByUserId = i.CreatedByUserId,
                CreatedByUsername = i.CreatedByUser.Username,
                CreatedAt = i.CreatedAt,
                ExpiresAt = i.ExpiresAt,
                UsedAt = i.UsedAt
            })
            .ToListAsync();
    }

    public async Task<(bool Success, InviteError? Error)> DeleteInviteAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (false, InviteErrors.TokenRequired);

        var invite = await _dbContext.InviteTokens
            .FirstOrDefaultAsync(i => i.Token == token);

        if (invite == null)
            return (false, InviteErrors.TokenInvalid);

        _dbContext.InviteTokens.Remove(invite);
        await _dbContext.SaveChangesAsync();

        return (true, null);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
