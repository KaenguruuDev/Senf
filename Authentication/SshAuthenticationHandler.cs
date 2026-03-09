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

		var hasUsernameHeader = Request.Headers.ContainsKey("X-SSH-Username");
		var hasSignatureHeader = Request.Headers.ContainsKey("X-SSH-Signature");
		var hasMessageHeader = Request.Headers.ContainsKey("X-SSH-Message");
		var hasNonceHeader = Request.Headers.ContainsKey("X-SSH-Nonce");

		if (!Request.Headers.TryGetValue("X-SSH-Username", out var username) ||
			!Request.Headers.TryGetValue("X-SSH-Signature", out var signature) ||
			!Request.Headers.TryGetValue("X-SSH-Message", out var message) ||
			!Request.Headers.TryGetValue("X-SSH-Nonce", out var nonce))
		{
			_logger.LogWarning(
				"SSH authentication failed: Missing required headers. Username: {HasUsername}, Signature: {HasSignature}, Message: {HasMessage}, Nonce: {HasNonce}",
				hasUsernameHeader,
				hasSignatureHeader,
				hasMessageHeader,
				hasNonceHeader);
			return AuthenticateResult.Fail("Authentication failed");
		}

		var usernameStr = username.ToString();
		var signatureStr = signature.ToString();
		var messageStr = message.ToString();
		var nonceStr = nonce.ToString();

		_logger.LogDebug(
			"SSH authentication headers received. Nonce length: {NonceLength}, message length: {MessageLength}, signature length: {SignatureLength}",
			nonceStr.Length, messageStr.Length, signatureStr.Length);

		if (!ValidHeaders(usernameStr, signatureStr, messageStr, nonceStr))
		{
			_logger.LogWarning("SSH authentication failed: Invalid headers");
			return AuthenticateResult.Fail("Authentication failed");
		}

		if (!long.TryParse(messageStr, out var timestamp))
		{
			_logger.LogWarning("SSH authentication failed: Invalid message timestamp");
			return AuthenticateResult.Fail("Authentication failed");
		}

		var messageTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
		var now = DateTimeOffset.UtcNow;
		var timeDiffMinutes = Math.Abs((now - messageTime).TotalMinutes);

		if (timeDiffMinutes > 5)
		{
			_logger.LogWarning(
				"SSH authentication failed: Timestamp out of range. Time difference: {TimeDiffMinutes} minutes",
				timeDiffMinutes);
			return AuthenticateResult.Fail("Authentication failed");
		}

		_logger.LogDebug(
			"SSH authentication timestamp validation passed. Time difference: {TimeDiffMinutes} minutes",
			timeDiffMinutes);

		var user = await dbContext.Users
			.AsNoTracking()
			.Include(u => u.SshKeys)
			.FirstOrDefaultAsync(u => u.Username == usernameStr);

		if (user == null)
		{
			_logger.LogWarning("SSH authentication failed: User not found");
			return AuthenticateResult.Fail("Authentication failed");
		}

		_logger.LogInformation("SSH authentication: User found with {KeyCount} SSH keys", user.SshKeys.Count);

		var pathAndQuery = $"{Request.Path}{Request.QueryString}";
		var messageToVerify = $"{messageStr}:{nonceStr}:{Request.Method}:{pathAndQuery}";

		var isValidSignature = false;
		int? authenticatedSshKeyId = null;

		if (user.SshKeys.Count != 0)
		{
			var keyIndex = 0;
			foreach (var key in user.SshKeys)
			{
				keyIndex++;
				_logger.LogDebug(
					"SSH authentication: Attempting signature verification with key {KeyIndex}/{TotalKeys}",
					keyIndex, user.SshKeys.Count);

				var result = sshAuthService.VerifySignature(key.PublicKey, messageToVerify, signatureStr, user.Username);
				if (result)
				{
					_logger.LogInformation(
						"SSH authentication: Signature verification successful with key {KeyIndex}",
						keyIndex);
					isValidSignature = true;
					authenticatedSshKeyId = key.Id;
					break;
				}

				_logger.LogDebug("SSH authentication: Signature verification failed for key {KeyIndex}", keyIndex);
			}

			if (!isValidSignature)
			{
				_logger.LogWarning(
					"SSH authentication failed: No valid signature found among {KeyCount} keys",
					user.SshKeys.Count);
			}
		}
		else
		{
			_logger.LogWarning("SSH authentication failed: User has no SSH keys configured");
		}

		if (!isValidSignature)
		{
			_logger.LogWarning("SSH authentication failed: Signature validation failed");
			return AuthenticateResult.Fail("Authentication failed");
		}

		if (!sshAuthService.TryMarkNonceAsUsed(nonceStr))
		{
			_logger.LogWarning("SSH authentication failed: Nonce already used");
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

		_logger.LogInformation("SSH authentication successful");
		return AuthenticateResult.Success(ticket);
	}

	private static bool ValidHeaders(string username, string signature, string message, string nonce)
		=> !string.IsNullOrWhiteSpace(username) &&
		   !string.IsNullOrWhiteSpace(signature) &&
		   !string.IsNullOrWhiteSpace(message) &&
		   !string.IsNullOrWhiteSpace(nonce);
}
