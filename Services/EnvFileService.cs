using Microsoft.EntityFrameworkCore;
using Senf.Data;
using Senf.Dtos;
using Senf.Models;

namespace Senf.Services;

public interface IEnvFileService
{
    Task<(bool Success, string Message, EnvFileResponse? File)> GetEnvFileAsync(int userId, string envName);
    Task<(bool Success, string Message, List<EnvFileResponse>? Files)> ListEnvFilesAsync(int userId);
    Task<(bool Success, string Message)> CreateEnvFileAsync(int userId, string envName, string content, int sshKeyId);
    Task<(bool Success, string Message)> UpdateEnvFileAsync(int userId, string envName, string content, int sshKeyId);
    Task<(bool Success, string Message)> DeleteEnvFileAsync(int userId, string envName);
}

public class EnvFileService(AppDbContext dbContext, IEncryptionService encryptionService)
    : IEnvFileService
{
    public async Task<(bool Success, string Message, EnvFileResponse? File)> GetEnvFileAsync(int userId, string envName)
    {
        if (string.IsNullOrWhiteSpace(envName))
            return (false, "Environment name cannot be empty", null);

        var envFile = await dbContext.EnvFiles
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Name == envName);

        if (envFile == null)
            return (false, $"Environment file '{envName}' not found", null);

        var decryptedContent = encryptionService.Decrypt(envFile.EncryptedContent);
        return (true, "Success", new EnvFileResponse
        {
            Id = envFile.Id,
            Name = envFile.Name,
            Content = decryptedContent,
            CreatedAt = envFile.CreatedAt,
            UpdatedAt = envFile.UpdatedAt,
            LastUpdatedByKeyId = envFile.LastUpdatedBySshKeyId
        });
    }

    public async Task<(bool Success, string Message, List<EnvFileResponse>? Files)> ListEnvFilesAsync(int userId)
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

        return (true, "Success", files);
    }

    public async Task<(bool Success, string Message)> CreateEnvFileAsync(int userId, string envName, string content, int sshKeyId)
    {
        if (string.IsNullOrWhiteSpace(envName))
            return (false, "Environment name cannot be empty");

        if (string.IsNullOrWhiteSpace(content))
            return (false, "Environment content cannot be empty");

        var existingFile = await dbContext.EnvFiles
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Name == envName);

        if (existingFile != null)
            return (false, $"Environment file '{envName}' already exists");

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

        return (true, $"Environment file '{envName}' created successfully");
    }

    public async Task<(bool Success, string Message)> UpdateEnvFileAsync(int userId, string envName, string content, int sshKeyId)
    {
        if (string.IsNullOrWhiteSpace(envName))
            return (false, "Environment name cannot be empty");

        if (string.IsNullOrWhiteSpace(content))
            return (false, "Environment content cannot be empty");

        var envFile = await dbContext.EnvFiles
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Name == envName);

        if (envFile == null)
            return (false, $"Environment file '{envName}' not found");

        envFile.EncryptedContent = encryptionService.Encrypt(content);
        envFile.UpdatedAt = DateTime.UtcNow;
        envFile.LastUpdatedBySshKeyId = sshKeyId;

        dbContext.EnvFiles.Update(envFile);
        await dbContext.SaveChangesAsync();

        return (true, $"Environment file '{envName}' updated successfully");
    }

    public async Task<(bool Success, string Message)> DeleteEnvFileAsync(int userId, string envName)
    {
        if (string.IsNullOrWhiteSpace(envName))
            return (false, "Environment name cannot be empty");

        var envFile = await dbContext.EnvFiles
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Name == envName);

        if (envFile == null)
            return (false, $"Environment file '{envName}' not found");

        dbContext.EnvFiles.Remove(envFile);
        await dbContext.SaveChangesAsync();

        return (true, $"Environment file '{envName}' deleted successfully");
    }
}
