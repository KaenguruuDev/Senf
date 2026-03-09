using System.Text;
using Microsoft.EntityFrameworkCore;
using Senf.Data;
using Senf.Dtos;
using Senf.Models;

namespace Senf.Services;

public interface IUserManagementService
{
	Task<(bool Success, string Message)> CreateUserAsync(string username, string publicSshKey, string? keyName = null);

	Task<(bool Success, SshKeyError? Error, SshKeyResponse?)> AddSshKeyAsync(int userId, string publicSshKey,
		string keyName);

	Task<(bool Success, string Message)> ListUsersAsync();
	Task<List<UserSummaryResponse>> GetUsersAsync();
	Task<(bool Success, SshKeyError? Error, SshKeyResponse?)> GetSshKeyAsync(int userId, int keyId);
	Task<(bool Success, SshKeyError? Error, List<SshKeyResponse>?)> GetUserSshKeysAsync(int userId);
	Task<(bool Success, SshKeyError? Error)> DeleteSshKeyAsync(int userId, int keyId);
	Task<(bool Success, SshKeyError? Error)> UpdateSshKeyNameAsync(int userId, int keyId, string newName);
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

		if (!_sshAuthService.IsSupportedPublicKey(publicSshKey))
			return (false, SshKeyErrors.InvalidKeyFormat.ToMessage());

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
				new SshKey
				{
					PublicKey = publicSshKey,
					Fingerprint = fingerprint,
					Name = keyName ?? "default",
					CreatedAt = DateTime.UtcNow
				}
			]
		};

		_dbContext.Users.Add(user);
		await _dbContext.SaveChangesAsync();

		return (true, $"User '{username}' created successfully with SSH key fingerprint: {fingerprint}");
	}

	public async Task<(bool Success, SshKeyError? Error, SshKeyResponse?)> AddSshKeyAsync(int userId,
		string publicSshKey,
		string keyName)
	{
		if (string.IsNullOrWhiteSpace(publicSshKey))
			return (false, SshKeyErrors.PublicKeyRequired, null);

		if (string.IsNullOrWhiteSpace(keyName))
			return (false, SshKeyErrors.NameRequired, null);

		if (!_sshAuthService.IsSupportedPublicKey(publicSshKey))
			return (false, SshKeyErrors.InvalidKeyFormat, null);

		var fingerprint = _sshAuthService.GetPublicKeyFingerprint(publicSshKey);
		if (string.IsNullOrEmpty(fingerprint))
			return (false, SshKeyErrors.InvalidKeyFormat, null);

		var user = await _dbContext.Users
			.FirstOrDefaultAsync(u => u.Id == userId);

		if (user == null)
			return (false, SshKeyErrors.UserNotFound, null);

		var existingKey = await _dbContext.SshKeys
			.FirstOrDefaultAsync(k => k.Fingerprint == fingerprint);

		if (existingKey != null)
			return (false, SshKeyErrors.DuplicateFingerprint, null);

		var existingName = await _dbContext.SshKeys
			.FirstOrDefaultAsync(k => k.UserId == userId && k.Name == keyName);

		if (existingName != null)
			return (false, SshKeyErrors.DuplicateName, null);

		var sshKey = new SshKey
		{
			PublicKey = publicSshKey,
			Fingerprint = fingerprint,
			Name = keyName,
			UserId = userId,
			CreatedAt = DateTime.UtcNow
		};

		_dbContext.SshKeys.Add(sshKey);
		try
		{
			await _dbContext.SaveChangesAsync();
		}
		catch (DbUpdateException)
		{
			var conflict = await ResolveSshKeyConflictAsync(userId, fingerprint, keyName, null);
			return (false, conflict, null);
		}

		var keyResponse = new SshKeyResponse
		{
			Id = sshKey.Id,
			PublicKey = sshKey.PublicKey,
			Fingerprint = sshKey.Fingerprint,
			Name = sshKey.Name,
			CreatedAt = sshKey.CreatedAt
		};

		return (true, null, keyResponse);
	}

	public async Task<(bool Success, string Message)> ListUsersAsync()
	{
		var users = await _dbContext.Users
			.Include(u => u.SshKeys)
			.OrderBy(u => u.Username)
			.ToListAsync();

		if (users.Count == 0)
			return (true, "No users found");

		var message = new StringBuilder("Users:\n");
		foreach (var user in users)
		{
			message.Append($"\n  {user.Username} (ID: {user.Id}, Created: {user.CreatedAt:yyyy-MM-dd HH:mm:ss})\n");
			if (user.SshKeys.Count != 0)
			{
				foreach (var key in user.SshKeys)
				{
					message.Append(
						$"    - {key.Name}: {key.Fingerprint} (Added: {key.CreatedAt:yyyy-MM-dd HH:mm:ss})\n");
				}
			}
			else
				message.Append("    - No SSH keys\n");
		}

		return (true, message.ToString());
	}

	public async Task<List<UserSummaryResponse>> GetUsersAsync()
	{
		var users = await _dbContext.Users
			.AsNoTracking()
			.OrderBy(u => u.Username)
			.Select(u => new UserSummaryResponse
			{
				Id = u.Id,
				Username = u.Username
			})
			.ToListAsync();

		return users;
	}

	public async Task<(bool Success, SshKeyError? Error, SshKeyResponse?)> GetSshKeyAsync(int userId, int keyId)
	{
		var sshKey = await _dbContext.SshKeys
			.FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId);

		if (sshKey == null)
			return (false, SshKeyErrors.NotFound, null);

		return (true, null, new SshKeyResponse
		{
			Id = sshKey.Id,
			PublicKey = sshKey.PublicKey,
			Fingerprint = sshKey.Fingerprint,
			Name = sshKey.Name,
			CreatedAt = sshKey.CreatedAt
		});
	}

	public async Task<(bool Success, SshKeyError? Error, List<SshKeyResponse>?)> GetUserSshKeysAsync(int userId)
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

		return (true, null, keys);
	}

	public async Task<(bool Success, SshKeyError? Error)> DeleteSshKeyAsync(int userId, int keyId)
	{
		var sshKey = await _dbContext.SshKeys
			.FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId);

		if (sshKey == null)
			return (false, SshKeyErrors.NotFound);
		var otherKeys = await _dbContext.SshKeys
			.CountAsync(k => k.UserId == userId && k.Id != keyId);

		if (otherKeys == 0)
			return (false, SshKeyErrors.CannotDeleteLastKey);

		_dbContext.SshKeys.Remove(sshKey);
		await _dbContext.SaveChangesAsync();

		return (true, null);
	}

	public async Task<(bool Success, SshKeyError? Error)> UpdateSshKeyNameAsync(int userId, int keyId, string newName)
	{
		if (string.IsNullOrWhiteSpace(newName))
			return (false, SshKeyErrors.NameRequired);

		var sshKey = await _dbContext.SshKeys
			.FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId);

		if (sshKey == null)
			return (false, SshKeyErrors.NotFound);

		var nameExists = await _dbContext.SshKeys
			.AnyAsync(k => k.UserId == userId && k.Name == newName && k.Id != keyId);

		if (nameExists)
			return (false, SshKeyErrors.DuplicateName);

		sshKey.Name = newName;
		_dbContext.SshKeys.Update(sshKey);
		try
		{
			await _dbContext.SaveChangesAsync();
		}
		catch (DbUpdateException)
		{
			var conflict = await ResolveSshKeyConflictAsync(userId, null, newName, keyId);
			return (false, conflict);
		}

		return (true, null);
	}

	private async Task<SshKeyError?> ResolveSshKeyConflictAsync(int userId, string? fingerprint, string? keyName,
		int? keyId)
	{
		if (!string.IsNullOrWhiteSpace(fingerprint))
		{
			var fingerprintExists = await _dbContext.SshKeys
				.AnyAsync(k => k.Fingerprint == fingerprint);
			if (fingerprintExists)
				return SshKeyErrors.DuplicateFingerprint;
		}

		if (!string.IsNullOrWhiteSpace(keyName))
		{
			var nameExists = await _dbContext.SshKeys
				.AnyAsync(k => k.UserId == userId && k.Name == keyName && k.Id != keyId);
			if (nameExists)
				return SshKeyErrors.DuplicateName;
		}

		return null;
	}
}