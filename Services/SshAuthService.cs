using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Parameters;

namespace Senf.Services;

public interface ISshAuthService
{
	bool VerifySignature(string publicKey, string message, string signature, string principal);
	string GetPublicKeyFingerprint(string publicKey);
	bool IsNonceUsed(string nonce);
	void MarkNonceAsUsed(string nonce);
	bool TryMarkNonceAsUsed(string nonce);
	bool IsSupportedPublicKey(string publicKey, out string errorMessage);
}

public class SshAuthService(ILogger<SshAuthService> logger) : ISshAuthService
{
	private readonly ConcurrentDictionary<string, DateTime> _usedNonces = new();
	private readonly TimeSpan _nonceExpiration = TimeSpan.FromMinutes(10);
	private DateTime _lastCleanup = DateTime.UtcNow;

	private readonly string _sshSignatureNamespace =
		Environment.GetEnvironmentVariable("SSH_SIGNATURE_NAMESPACE") ?? "senf-api-auth";

	private readonly string _sshKeygenPath = Environment.GetEnvironmentVariable("SSH_KEYGEN_PATH") ?? "ssh-keygen";
	private const int VerifyTimeoutMs = 5000;

	public bool VerifySignature(string publicKey, string message, string signature, string principal)
	{
		if (string.IsNullOrWhiteSpace(publicKey) ||
		    string.IsNullOrWhiteSpace(message) ||
		    string.IsNullOrWhiteSpace(signature) ||
		    string.IsNullOrWhiteSpace(principal))
		{
			logger.LogWarning("VerifySignature called with invalid parameters");
			return false;
		}

		if (!IsValidSignerPrincipal(principal))
		{
			logger.LogWarning("VerifySignature failed: principal contains unsupported characters");
			return false;
		}

		if (TryDecodeSignatureArmoredBlock(signature, out var signatureArmored))
			return VerifyWithOpenSsh(publicKey, message, signatureArmored, principal);

		logger.LogWarning("VerifySignature failed: invalid signature payload format");
		return false;
	}

	public string GetPublicKeyFingerprint(string publicKey)
	{
		try
		{
			var keyParts = publicKey.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (keyParts.Length < 2)
				return string.Empty;

			var keyBytes = Convert.FromBase64String(keyParts[1]);
			var hash = SHA256.HashData(keyBytes);
			return $"SHA256:{Convert.ToBase64String(hash).TrimEnd('=')}";
		}
		catch
		{
			return string.Empty;
		}
	}

	public bool IsNonceUsed(string nonce)
	{
		CleanupExpiredNonces();
		return _usedNonces.ContainsKey(nonce);
	}

	public void MarkNonceAsUsed(string nonce)
	{
		_usedNonces.TryAdd(nonce, DateTime.UtcNow);
		CleanupExpiredNonces();
	}

	public bool TryMarkNonceAsUsed(string nonce)
	{
		CleanupExpiredNonces();
		return _usedNonces.TryAdd(nonce, DateTime.UtcNow);
	}

	public bool IsSupportedPublicKey(string publicKey, out string errorMessage)
	{
		errorMessage = string.Empty;

		if (string.IsNullOrWhiteSpace(publicKey))
		{
			errorMessage = "SSH public key cannot be empty";
			return false;
		}

		try
		{
			var keyParts = publicKey.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (keyParts.Length < 2)
			{
				errorMessage = "Invalid SSH key format";
				return false;
			}

			var keyType = keyParts[0];
			var keyBytes = Convert.FromBase64String(keyParts[1]);

			switch (keyType)
			{
				case "ssh-ed25519":
					if (ParseSshEd25519PublicKey(keyBytes))
						return true;
					errorMessage = "Invalid ssh-ed25519 key format";
					return false;
				case "ssh-rsa":
					var rsaKey = ParseSshRsaPublicKey(keyBytes);
					if (rsaKey == null)
					{
						errorMessage = "Invalid ssh-rsa key format";
						return false;
					}

					if (rsaKey.Modulus.BitLength >= 2048)
						return true;

					errorMessage = "RSA keys must be at least 2048 bits";
					return false;
				default:
					errorMessage = $"Unsupported SSH key type '{keyType}'. Supported types: ssh-rsa, ssh-ed25519";
					return false;
			}
		}
		catch
		{
			errorMessage = "Invalid SSH key format";
			return false;
		}
	}

	private bool VerifyWithOpenSsh(string publicKey, string message, string signatureArmored, string principal)
	{
		var workDir = Path.Combine(Path.GetTempPath(), $"senf-ssh-verify-{Guid.NewGuid():N}");
		try
		{
			Directory.CreateDirectory(workDir);
			SetSecureUnixPermissionsIfSupported(
				workDir,
				UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

			var allowedSignersPath = Path.Combine(workDir, "allowed_signers");
			var signaturePath = Path.Combine(workDir, "signature.sig");
			var allowedSignerLine =
				$"{principal} namespaces=\"{_sshSignatureNamespace}\" {publicKey.Trim()}{Environment.NewLine}";

			File.WriteAllText(allowedSignersPath, allowedSignerLine, Encoding.UTF8);
			SetSecureUnixPermissionsIfSupported(allowedSignersPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

			File.WriteAllText(signaturePath, signatureArmored, Encoding.UTF8);
			SetSecureUnixPermissionsIfSupported(signaturePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

			var processStartInfo = new ProcessStartInfo
			{
				FileName = _sshKeygenPath,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};

			processStartInfo.ArgumentList.Add("-Y");
			processStartInfo.ArgumentList.Add("verify");
			processStartInfo.ArgumentList.Add("-f");
			processStartInfo.ArgumentList.Add(allowedSignersPath);
			processStartInfo.ArgumentList.Add("-I");
			processStartInfo.ArgumentList.Add(principal);
			processStartInfo.ArgumentList.Add("-n");
			processStartInfo.ArgumentList.Add(_sshSignatureNamespace);
			processStartInfo.ArgumentList.Add("-s");
			processStartInfo.ArgumentList.Add(signaturePath);

			using var process = Process.Start(processStartInfo);
			if (process == null)
			{
				logger.LogError("VerifySignature failed: could not start ssh-keygen");
				return false;
			}

			process.StandardInput.Write(message);
			process.StandardInput.Close();

			if (!process.WaitForExit(VerifyTimeoutMs))
			{
				process.Kill(true);
				logger.LogWarning("VerifySignature failed: ssh-keygen verification timed out after {TimeoutMs} ms", VerifyTimeoutMs);
				return false;
			}

			if (process.ExitCode != 0)
			{
				logger.LogWarning("VerifySignature failed: ssh-keygen rejected signature. ExitCode: {ExitCode}", process.ExitCode);
				return false;
			}

			return true;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "VerifySignature failed: OpenSSH verification error");
			return false;
		}
		finally
		{
			try
			{
				if (Directory.Exists(workDir))
					Directory.Delete(workDir, true);
			}
			catch
			{
				// Best effort cleanup.
			}
		}
	}

	private void CleanupExpiredNonces()
	{
		if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromMinutes(1))
			return;

		_lastCleanup = DateTime.UtcNow;
		var expiredKeys = _usedNonces
			.Where(kvp => DateTime.UtcNow - kvp.Value > _nonceExpiration)
			.Select(kvp => kvp.Key)
			.ToList();

		foreach (var key in expiredKeys)
			_usedNonces.TryRemove(key, out _);
	}

	private static bool TryDecodeSignatureArmoredBlock(string signaturePayload, out string signatureArmored)
	{
		signatureArmored = string.Empty;

		string candidate;
		try
		{
			var decoded = Convert.FromBase64String(signaturePayload);
			candidate = Encoding.UTF8.GetString(decoded);
		}
		catch (FormatException)
		{
			candidate = signaturePayload;
		}

		if (!candidate.Contains("BEGIN SSH SIGNATURE", StringComparison.Ordinal) ||
		    !candidate.Contains("END SSH SIGNATURE", StringComparison.Ordinal))
			return false;

		signatureArmored = candidate;
		return true;
	}

	private static bool IsValidSignerPrincipal(string principal)
		=> principal.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '@');

	private static void SetSecureUnixPermissionsIfSupported(string path, UnixFileMode mode)
	{
		if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
			File.SetUnixFileMode(path, mode);
	}

	private static bool ParseSshEd25519PublicKey(byte[] keyBytes)
	{
		try
		{
			using var ms = new MemoryStream(keyBytes);
			using var reader = new BinaryReader(ms);

			var algorithmLength = ReadSshInt(reader);
			if (algorithmLength <= 0 || algorithmLength > 256)
				return false;

			var algorithm = Encoding.ASCII.GetString(reader.ReadBytes(algorithmLength));
			if (algorithm != "ssh-ed25519")
				return false;

			var keyLength = ReadSshInt(reader);
			if (keyLength != 32)
				return false;

			var key = reader.ReadBytes(keyLength);
			return key.Length == 32;
		}
		catch
		{
			return false;
		}
	}

	private static RsaKeyParameters? ParseSshRsaPublicKey(byte[] keyBytes)
	{
		try
		{
			using var ms = new MemoryStream(keyBytes);
			using var reader = new BinaryReader(ms);

			var algorithmLength = ReadSshInt(reader);
			if (algorithmLength <= 0 || algorithmLength > 256)
				return null;

			var algorithm = Encoding.ASCII.GetString(reader.ReadBytes(algorithmLength));
			if (algorithm != "ssh-rsa")
				return null;

			var exponentLength = ReadSshInt(reader);
			if (exponentLength <= 0 || exponentLength > 8192)
				return null;

			var exponent = new Org.BouncyCastle.Math.BigInteger(1, reader.ReadBytes(exponentLength));

			var modulusLength = ReadSshInt(reader);
			if (modulusLength <= 0 || modulusLength > 8192)
				return null;

			var modulus = new Org.BouncyCastle.Math.BigInteger(1, reader.ReadBytes(modulusLength));
			return new RsaKeyParameters(false, modulus, exponent);
		}
		catch
		{
			return null;
		}
	}

	private static int ReadSshInt(BinaryReader reader)
	{
		var bytes = reader.ReadBytes(4);
		if (bytes.Length != 4)
			return -1;

		if (BitConverter.IsLittleEndian)
			Array.Reverse(bytes);

		var value = BitConverter.ToInt32(bytes, 0);
		return value is < 0 or > 1_000_000 ? -1 : value;
	}
}
