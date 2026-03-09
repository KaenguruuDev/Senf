using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Senf.Data;
using Senf.Services;
using ClaimTypes = System.Security.Claims.ClaimTypes;

namespace Senf.Authentication;

public class SshAuthenticationOptions : AuthenticationSchemeOptions
{
	public const string DefaultScheme = "SshKey";
	public string Scheme => DefaultScheme;
}

public class SshAuthenticationHandler(
	IOptionsMonitor<SshAuthenticationOptions> options,
	ILoggerFactory loggerFactory,
	UrlEncoder encoder,
	AppDbContext dbContext,
	ISshAuthService sshAuthService)
	: AuthenticationHandler<SshAuthenticationOptions>(options, loggerFactory, encoder)
{
	private readonly ILogger<SshAuthenticationHandler> _logger = loggerFactory.CreateLogger<SshAuthenticationHandler>();

	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		_logger.LogInformation("SSH authentication attempt started for request path: {RequestPath}", Request.Path);

		if (!Request.Headers.TryGetValue("X-SSH-Username", out var username) ||
			!Request.Headers.TryGetValue("X-SSH-Signature", out var signature) ||
			!Request.Headers.TryGetValue("X-SSH-Message", out var message) ||
			!Request.Headers.TryGetValue("X-SSH-Nonce", out var nonce))
		{
			_logger.LogWarning(
				"SSH authentication failed: Missing required headers. Username: {HasUsername}, Signature: {HasSignature}, Message: {HasMessage}, Nonce: {HasNonce}",
				Request.Headers.ContainsKey("X-SSH-Username"),
				Request.Headers.ContainsKey("X-SSH-Signature"),
				Request.Headers.ContainsKey("X-SSH-Message"),
				Request.Headers.ContainsKey("X-SSH-Nonce"));
			return AuthenticateResult.Fail("Authentication failed");
		}

		var usernameStr = username.ToString();
		var signatureStr = signature.ToString();
		var messageStr = message.ToString();
		var nonceStr = nonce.ToString();

		_logger.LogDebug(
			"SSH authentication headers received for username: {Username}, nonce length: {NonceLength}, message length: {MessageLength}, signature length: {SignatureLength}",
			usernameStr, nonceStr.Length, messageStr.Length, signatureStr.Length);

		if (!ValidHeaders(usernameStr, signatureStr, messageStr, nonceStr))
		{
			_logger.LogWarning("SSH authentication failed: Invalid headers for username {Username}", usernameStr);
			return AuthenticateResult.Fail("Authentication failed");
		}

		if (!long.TryParse(messageStr, out var timestamp))
		{
			_logger.LogWarning(
				"SSH authentication failed: Invalid message timestamp for username {Username}, value: {MessageValue}",
				usernameStr, messageStr);
			return AuthenticateResult.Fail("Authentication failed");
		}

		var messageTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
		var now = DateTimeOffset.UtcNow;
		var timeDiffMinutes = Math.Abs((now - messageTime).TotalMinutes);

		if (timeDiffMinutes > 5)
		{
			_logger.LogWarning(
				"SSH authentication failed: Timestamp out of range for username {Username}. Time difference: {TimeDiffMinutes} minutes",
				usernameStr, timeDiffMinutes);
			return AuthenticateResult.Fail("Authentication failed");
		}

		_logger.LogDebug(
			"SSH authentication: Timestamp validation passed for username {Username}. Time difference: {TimeDiffMinutes} minutes",
			usernameStr, timeDiffMinutes);

		var user = await dbContext.Users
			.AsNoTracking()
			.Include(u => u.SshKeys)
			.FirstOrDefaultAsync(u => u.Username == usernameStr);

		if (user == null)
		{
			_logger.LogWarning("SSH authentication failed: User not found. Username: {Username}", usernameStr);
			return AuthenticateResult.Fail("Authentication failed");
		}

		_logger.LogInformation("SSH authentication: User found with {KeyCount} SSH keys. Username: {Username}",
			user.SshKeys.Count, usernameStr);

		var pathAndQuery = $"{Request.Path}{Request.QueryString}";
		var messageToVerify = $"{messageStr}:{nonceStr}:{Request.Method}:{pathAndQuery}";

		var isValidSignature = false;
		int? authenticatedSshKeyId = null;

		if (user.SshKeys.Count != 0)
		{
			int keyIndex = 0;
			foreach (var key in user.SshKeys)
			{
				keyIndex++;
				var keyFingerprint = sshAuthService.GetPublicKeyFingerprint(key.PublicKey);
				_logger.LogDebug(
					"SSH authentication: Attempting signature verification with key {KeyIndex}/{TotalKeys}. Fingerprint: {Fingerprint}",
					keyIndex, user.SshKeys.Count, keyFingerprint);

				var result = sshAuthService.VerifySignature(key.PublicKey, messageToVerify, signatureStr, user.Username);
				if (result)
				{
					_logger.LogInformation(
						"SSH authentication: Signature verification successful for username {Username} with key {KeyIndex}. Fingerprint: {Fingerprint}",
						usernameStr, keyIndex, keyFingerprint);
					isValidSignature = true;
					authenticatedSshKeyId = key.Id;
					break;
				}
				else
				{
					_logger.LogDebug(
						"SSH authentication: Signature verification failed for key {KeyIndex}. Fingerprint: {Fingerprint}",
						keyIndex, keyFingerprint);
				}
			}

			if (!isValidSignature)
			{
				_logger.LogWarning(
					"SSH authentication failed: No valid signature found among {KeyCount} keys for username {Username}",
					user.SshKeys.Count, usernameStr);
			}
		}
		else
		{
			_logger.LogWarning("SSH authentication failed: User {Username} has no SSH keys configured", usernameStr);
		}


		if (!isValidSignature)
		{
			_logger.LogWarning("SSH authentication failed: Signature validation failed for username {Username}",
				usernameStr);
			return AuthenticateResult.Fail("Authentication failed");
		}

		if (!sshAuthService.TryMarkNonceAsUsed(nonceStr))
		{
			_logger.LogWarning("SSH authentication failed: Nonce already used for username {Username}", usernameStr);
			return AuthenticateResult.Fail("Authentication failed");
		}

		var claims = new[]
		{
			new Claim(ClaimTypes.Name, user.Username),
			new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
			new Claim("SshKeyId", authenticatedSshKeyId?.ToString() ?? "0")
		};

		var identity = new ClaimsIdentity(claims, Scheme.Name);
		var principal = new ClaimsPrincipal(identity);
		var ticket = new AuthenticationTicket(principal, Scheme.Name);

		_logger.LogInformation("SSH authentication successful for username {Username}", usernameStr);
		return AuthenticateResult.Success(ticket);
	}

	private static bool ValidHeaders(string username, string signature, string message, string nonce)
		=> !string.IsNullOrWhiteSpace(username) &&
		   !string.IsNullOrWhiteSpace(signature) &&
		   !string.IsNullOrWhiteSpace(message) &&
		   !string.IsNullOrWhiteSpace(nonce);
}

