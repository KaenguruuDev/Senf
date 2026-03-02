using Microsoft.EntityFrameworkCore;
using Senf.Data;
using Senf.Dtos;
using Senf.Models;

namespace Senf.Services;

public interface IUserManagementService
{
	Task<(bool Success, string Message)> CreateUserAsync(string username, string publicSshKey, string? keyName = null);

	Task<(bool Success, string Message, SshKeyResponse?)> AddSshKeyAsync(int userId, string publicSshKey,
		string keyName);

	Task<(bool Success, string Message)> ListUsersAsync();
	Task<(bool Success, string Message, SshKeyResponse?)> GetSshKeyAsync(int userId, int keyId);
	Task<(bool Success, string Message, List<SshKeyResponse>?)> GetUserSshKeysAsync(int userId);
	Task<(bool Success, string Message)> DeleteSshKeyAsync(int userId, int keyId);
	Task<(bool Success, string Message)> UpdateSshKeyNameAsync(int userId, int keyId, string newName);
}

public class UserManagementService : IUserManagementService
{
	private readonly AppDbContext _dbContext;
	private readonly ISshAuthService _sshAuthService;

	public UserManagementService(AppDbContext dbContext, ISshAuthService sshAuthService)
	{
		_dbContext = dbContext;
		_sshAuthService = sshAuthService;
	}

	public async Task<(bool Success, string Message)> CreateUserAsync(string username, string publicSshKey,
		string? keyName = null)
	{
		if (string.IsNullOrWhiteSpace(username))
			return (false, "Username cannot be empty");

		if (string.IsNullOrWhiteSpace(publicSshKey))
			return (false, "SSH public key cannot be empty");
		
		if (!_sshAuthService.IsSupportedPublicKey(publicSshKey, out var keyValidationError))
			return (false, keyValidationError);

		var fingerprint = _sshAuthService.GetPublicKeyFingerprint(publicSshKey);
		if (string.IsNullOrEmpty(fingerprint))
			return (false, "Invalid SSH public key format or unable to generate fingerprint");

		var existingUser = await _dbContext.Users
			.FirstOrDefaultAsync(u => u.Username == username);

		if (existingUser != null)
			return (false, $"User '{username}' already exists");

		var user = new User
		{
			Username = username,
			CreatedAt = DateTime.UtcNow,
			SshKeys =
			[
				new()
				{
					PublicKey = publicSshKey,
					Fingerprint = fingerprint,
					Name = keyName ?? $"default",
					CreatedAt = DateTime.UtcNow
				}
			]
		};

		_dbContext.Users.Add(user);
		await _dbContext.SaveChangesAsync();

		return (true, $"User '{username}' created successfully with SSH key fingerprint: {fingerprint}");
	}

	public async Task<(bool Success, string Message, SshKeyResponse?)> AddSshKeyAsync(int userId, string publicSshKey,
		string keyName)
	{
		if (string.IsNullOrWhiteSpace(publicSshKey))
			return (false, "SSH public key cannot be empty", null);

		if (string.IsNullOrWhiteSpace(keyName))
			return (false, "Key name cannot be empty", null);
		
		if (!_sshAuthService.IsSupportedPublicKey(publicSshKey, out var keyValidationError))
			return (false, keyValidationError, null);

		var fingerprint = _sshAuthService.GetPublicKeyFingerprint(publicSshKey);
		if (string.IsNullOrEmpty(fingerprint))
			return (false, "Invalid SSH public key format or unable to generate fingerprint", null);

		var user = await _dbContext.Users
			.FirstOrDefaultAsync(u => u.Id == userId);

		if (user == null)
			return (false, $"User with ID {userId} not found", null);

		var existingKey = await _dbContext.SshKeys
			.FirstOrDefaultAsync(k => k.Fingerprint == fingerprint && k.UserId == userId);

		if (existingKey != null)
			return (false, "SSH key with this fingerprint already exists for this user", null);

		var sshKey = new SshKey
		{
			PublicKey = publicSshKey,
			Fingerprint = fingerprint,
			Name = keyName,
			UserId = userId,
			CreatedAt = DateTime.UtcNow
		};

		_dbContext.SshKeys.Add(sshKey);
		await _dbContext.SaveChangesAsync();

		var keyResponse = new SshKeyResponse
		{
			Id = sshKey.Id,
			PublicKey = sshKey.PublicKey,
			Fingerprint = sshKey.Fingerprint,
			Name = sshKey.Name,
			CreatedAt = sshKey.CreatedAt
		};

		return (true, $"SSH key '{keyName}' added to user '{user.Username}' with fingerprint: {fingerprint}",
			keyResponse);
	}

	public async Task<(bool Success, string Message)> ListUsersAsync()
	{
		var users = await _dbContext.Users
			.Include(u => u.SshKeys)
			.OrderBy(u => u.Username)
			.ToListAsync();

		if (!users.Any())
			return (true, "No users found");

		var message = "Users:\n";
		foreach (var user in users)
		{
			message += $"\n  {user.Username} (ID: {user.Id}, Created: {user.CreatedAt:yyyy-MM-dd HH:mm:ss})\n";
			if (user.SshKeys.Any())
			{
				foreach (var key in user.SshKeys)
				{
					message += $"    - {key.Name}: {key.Fingerprint} (Added: {key.CreatedAt:yyyy-MM-dd HH:mm:ss})\n";
				}
			}
			else
			{
				message += "    - No SSH keys\n";
			}
		}

		return (true, message);
	}

	public async Task<(bool Success, string Message, SshKeyResponse?)> GetSshKeyAsync(int userId, int keyId)
	{
		var sshKey = await _dbContext.SshKeys
			.FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId);

		if (sshKey == null)
			return (false, "SSH key not found", null);

		return (true, "Success", new SshKeyResponse
		{
			Id = sshKey.Id,
			PublicKey = sshKey.PublicKey,
			Fingerprint = sshKey.Fingerprint,
			Name = sshKey.Name,
			CreatedAt = sshKey.CreatedAt
		});
	}

	public async Task<(bool Success, string Message, List<SshKeyResponse>?)> GetUserSshKeysAsync(int userId)
	{
		var sshKeys = await _dbContext.SshKeys
			.Where(k => k.UserId == userId)
			.OrderByDescending(k => k.CreatedAt)
			.ToListAsync();

		var keys = sshKeys.Select(k => new SshKeyResponse
		{
			Id = k.Id,
			PublicKey = k.PublicKey,
			Fingerprint = k.Fingerprint,
			Name = k.Name,
			CreatedAt = k.CreatedAt
		}).ToList();

		return (true, "Success", keys);
	}

	public async Task<(bool Success, string Message)> DeleteSshKeyAsync(int userId, int keyId)
	{
		var sshKey = await _dbContext.SshKeys
			.FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId);

		if (sshKey == null)
			return (false, "SSH key not found");
		
		var otherKeys = await _dbContext.SshKeys
			.CountAsync(k => k.UserId == userId && k.Id != keyId);

		if (otherKeys == 0)
			return (false, "Cannot delete the last SSH key. At least one key must remain.");

		_dbContext.SshKeys.Remove(sshKey);
		await _dbContext.SaveChangesAsync();

		return (true, "SSH key deleted successfully");
	}

	public async Task<(bool Success, string Message)> UpdateSshKeyNameAsync(int userId, int keyId, string newName)
	{
		if (string.IsNullOrWhiteSpace(newName))
			return (false, "Key name cannot be empty");

		var sshKey = await _dbContext.SshKeys
			.FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId);

		if (sshKey == null)
			return (false, "SSH key not found");

		sshKey.Name = newName;
		_dbContext.SshKeys.Update(sshKey);
		await _dbContext.SaveChangesAsync();

		return (true, "SSH key name updated successfully");
	}

}
