using System.Security.Claims;
using System.Text;
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
	private const string AuthenticationFailedMessage = "Authentication failed";

	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		if (!Request.Headers.TryGetValue("X-SSH-Username", out var username) ||
		    !Request.Headers.TryGetValue("X-SSH-Signature", out var signature) ||
		    !Request.Headers.TryGetValue("X-SSH-Message", out var message) ||
		    !Request.Headers.TryGetValue("X-SSH-Nonce", out var nonce))
		{
			_logger.LogDebug(
				"SSH authentication skipped: Missing required headers");
			return AuthenticateResult.NoResult();
		}

		var usernameStr = username.ToString();
		var signatureStr = signature.ToString();
		var messageStr = message.ToString();
		var nonceStr = nonce.ToString();

		if (!ValidHeaders(usernameStr, signatureStr, messageStr, nonceStr))
		{
			_logger.LogWarning("SSH authentication failed: Invalid headers");
			return AuthenticateResult.Fail(AuthenticationFailedMessage);
		}

		if (!long.TryParse(messageStr, out var timestamp))
		{
			_logger.LogWarning("SSH authentication failed: Invalid message timestamp");
			return AuthenticateResult.Fail(AuthenticationFailedMessage);
		}

		var messageTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
		var now = DateTimeOffset.UtcNow;
		var timeDiffMinutes = Math.Abs((now - messageTime).TotalMinutes);

		if (timeDiffMinutes > 1)
		{
			_logger.LogWarning(
				"SSH authentication failed: Timestamp out of range. Time difference: {TimeDiffMinutes} minutes",
				timeDiffMinutes);
			return AuthenticateResult.Fail(AuthenticationFailedMessage);
		}

		var user = await dbContext.Users
			.AsNoTracking()
			.Include(u => u.SshKeys)
			.FirstOrDefaultAsync(u => u.Username == usernameStr);

		if (user == null)
		{
			_logger.LogWarning("SSH authentication failed: User not found");
			return AuthenticateResult.Fail(AuthenticationFailedMessage);
		}

		var pathAndQuery = $"{Request.Path}{Request.QueryString}";
		var messageToVerify = $"{messageStr}:{nonceStr}:{Request.Method}:{pathAndQuery}";

		var isValidSignature = false;
		int? authenticatedSshKeyId = null;

		if (user.SshKeys.Count == 0)
		{
			_logger.LogWarning("SSH authentication failed: User has no SSH keys configured");
			return AuthenticateResult.Fail(AuthenticationFailedMessage);
		}

		var keyIndex = 0;
		foreach (var key in user.SshKeys)
		{
			_logger.LogDebug(
				"SSH authentication: Attempting signature verification with key {KeyIndex}/{TotalKeys}",
				keyIndex, user.SshKeys.Count);

			keyIndex++;
			var success =
				sshAuthService.VerifySignature(key.PublicKey, messageToVerify, signatureStr);
			if (!success)
				continue;

			isValidSignature = true;
			authenticatedSshKeyId = key.Id;
			break;
		}

		if (!isValidSignature || !sshAuthService.TryMarkNonceAsUsed(nonceStr))
			return AuthenticateResult.Fail(AuthenticationFailedMessage);

		var claims = new[]
		{
			new Claim(ClaimTypes.Name, user.Username),
			new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
			new Claim("SshKeyId", authenticatedSshKeyId?.ToString() ?? "0")
		};

		var identity = new ClaimsIdentity(claims, Scheme.Name);
		var principal = new ClaimsPrincipal(identity);
		var ticket = new AuthenticationTicket(principal, Scheme.Name);

		return AuthenticateResult.Success(ticket);
	}

	private static bool ValidHeaders(string username, string signature, string message, string nonce)
		=> !string.IsNullOrWhiteSpace(username) &&
		   !string.IsNullOrWhiteSpace(signature) &&
		   !string.IsNullOrWhiteSpace(message) &&
		   !string.IsNullOrWhiteSpace(nonce);
}