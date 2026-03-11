using Microsoft.EntityFrameworkCore;
using Senf.Data;
using Senf.Dtos;
using Senf.Models;

namespace Senf.Services;

public interface IEnvFileService
{
    Task<(bool Success, EnvFileError? Error, EnvFileResponse? File)> GetEnvFileAsync(int userId, string envName);
    Task<(bool Success, EnvFileError? Error, List<EnvFileResponse>? Files)> ListEnvFilesAsync(int userId);
    Task<(bool Success, EnvFileError? Error)> CreateEnvFileAsync(int userId, string envName, string content, int sshKeyId);
    Task<(bool Success, EnvFileError? Error)> UpdateEnvFileAsync(int userId, string envName, string content, int sshKeyId);
    Task<(bool Success, EnvFileError? Error)> DeleteEnvFileAsync(int userId, string envName);
    Task<bool> ExistsEnvFileAsync(int userId, string envName);
}

public class EnvFileService(AppDbContext dbContext, IEncryptionService encryptionService)
    : IEnvFileService
{
    public async Task<(bool Success, EnvFileError? Error, EnvFileResponse? File)> GetEnvFileAsync(int userId, string envName)
    {
        if (string.IsNullOrWhiteSpace(envName))
            return (false, EnvFileErrors.NameRequired, null);

        var envFile = await dbContext.EnvFiles
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Name == envName);

        if (envFile == null)
            return (false, EnvFileErrors.NotFound, null);

        var decryptedContent = encryptionService.Decrypt(envFile.EncryptedContent);
        return (true, null, new EnvFileResponse
        {
            Id = envFile.Id,
            Name = envFile.Name,
            Content = decryptedContent,
            CreatedAt = envFile.CreatedAt,
            UpdatedAt = envFile.UpdatedAt,
            LastUpdatedByKeyId = envFile.LastUpdatedBySshKeyId
        });
    }

    public async Task<(bool Success, EnvFileError? Error, List<EnvFileResponse>? Files)> ListEnvFilesAsync(int userId)
    {
        var envFiles = await dbContext.EnvFiles
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.UpdatedAt)
            .ToListAsync();

        var files = envFiles.Select(e => new EnvFileResponse
        {
            Id = e.Id,
            Name = e.Name,
            Content = encryptionService.Decrypt(e.EncryptedContent),
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            LastUpdatedByKeyId = e.LastUpdatedBySshKeyId
        }).ToList();

        return (true, null, files);
    }

    public async Task<(bool Success, EnvFileError? Error)> CreateEnvFileAsync(int userId, string envName, string content, int sshKeyId)
    {
        if (string.IsNullOrWhiteSpace(envName))
            return (false, EnvFileErrors.NameRequired);

        if (string.IsNullOrWhiteSpace(content))
            return (false, EnvFileErrors.ContentRequired);

        var existingFile = await dbContext.EnvFiles
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Name == envName);

        if (existingFile != null)
            return (false, EnvFileErrors.AlreadyExists);

        var encryptedContent = encryptionService.Encrypt(content);
        var envFile = new EnvFile
        {
            Name = envName,
            EncryptedContent = encryptedContent,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastUpdatedBySshKeyId = sshKeyId
        };

        dbContext.EnvFiles.Add(envFile);
        await dbContext.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, EnvFileError? Error)> UpdateEnvFileAsync(int userId, string envName, string content, int sshKeyId)
    {
        if (string.IsNullOrWhiteSpace(envName))
            return (false, EnvFileErrors.NameRequired);

        if (string.IsNullOrWhiteSpace(content))
            return (false, EnvFileErrors.ContentRequired);

        var envFile = await dbContext.EnvFiles
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Name == envName);

        if (envFile == null)
            return (false, EnvFileErrors.NotFound);

        envFile.EncryptedContent = encryptionService.Encrypt(content);
        envFile.UpdatedAt = DateTime.UtcNow;
        envFile.LastUpdatedBySshKeyId = sshKeyId;

        dbContext.EnvFiles.Update(envFile);
        await dbContext.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, EnvFileError? Error)> DeleteEnvFileAsync(int userId, string envName)
    {
        if (string.IsNullOrWhiteSpace(envName))
            return (false, EnvFileErrors.NameRequired);

        var envFile = await dbContext.EnvFiles
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Name == envName);

        if (envFile == null)
            return (false, EnvFileErrors.NotFound);

        dbContext.EnvFiles.Remove(envFile);
        await dbContext.SaveChangesAsync();

        return (true, null);
    }

    public async Task<bool> ExistsEnvFileAsync(int userId, string envName)
    {
        if (string.IsNullOrWhiteSpace(envName))
            return false;

        return await dbContext.EnvFiles
            .AnyAsync(e => e.UserId == userId && e.Name == envName);
    }
}
