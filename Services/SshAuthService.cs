using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Senf.Services;

public interface ISshAuthService
{
	bool VerifySignature(string publicKey, string message, string signature);
	string GetPublicKeyFingerprint(string publicKey);
	bool IsNonceUsed(string nonce);
	void MarkNonceAsUsed(string nonce);
	bool TryMarkNonceAsUsed(string nonce);
	bool IsSupportedPublicKey(string publicKey, out string errorMessage);
}

public class SshAuthService(ILogger<SshAuthService> logger) : ISshAuthService
{
	private static readonly byte[] SshSigMagic = "SSHSIG"u8.ToArray();
	private readonly ConcurrentDictionary<string, DateTime> _usedNonces = new();
	private readonly TimeSpan _nonceExpiration = TimeSpan.FromMinutes(10);
	private DateTime _lastCleanup = DateTime.UtcNow;

	public bool VerifySignature(string publicKey, string message, string signature)
	{
		if (string.IsNullOrWhiteSpace(publicKey) ||
			string.IsNullOrWhiteSpace(message) ||
			string.IsNullOrWhiteSpace(signature))
		{
			logger.LogWarning(
				"VerifySignature called with null or whitespace parameters. PublicKey null: {PublicKeyNull}, Message null: {MessageNull}, Signature null: {SignatureNull}",
				string.IsNullOrWhiteSpace(publicKey), string.IsNullOrWhiteSpace(message),
				string.IsNullOrWhiteSpace(signature));
			return false;
		}

		try
		{
			var keyParts = publicKey.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (keyParts.Length < 2)
			{
				logger.LogWarning(
					"VerifySignature: Invalid public key format. Expected at least 2 parts, got {PartCount}",
					keyParts.Length);
				return false;
			}

			var keyType = keyParts[0];
			logger.LogDebug("VerifySignature: Verifying signature with key type: {KeyType}", keyType);

			try
			{
				var keyBytes = Convert.FromBase64String(keyParts[1]);
				logger.LogDebug("VerifySignature: Decoded key bytes successfully, length: {KeyBytesLength}",
					keyBytes.Length);

				var result = keyType switch
				{
					"ssh-rsa" => VerifyRsaSignature(keyBytes, message, signature),
					"ssh-ed25519" => VerifyEd25519Signature(keyBytes, message, signature),
					_ => WarnInvalidKeyType(keyType)
				};

				if (!result)
				{
					logger.LogDebug("VerifySignature: Signature verification returned false for key type: {KeyType}",
						keyType);
				}

				return result;
			}
			catch (FormatException)
			{
				logger.LogWarning("VerifySignature: Failed to decode base64 public key or signature");
				return false;
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "VerifySignature: Unexpected error during signature verification");
			return false;
		}
	}

	private bool VerifyRsaSignature(byte[] keyBytes, string message, string signature)
	{
		try
		{
			logger.LogDebug(
				"VerifyRsaSignature: Starting RSA signature verification. Key bytes length: {KeyBytesLength}",
				keyBytes.Length);

			var rsaKey = ParseSshRsaPublicKey(keyBytes);
			if (rsaKey == null)
			{
				logger.LogDebug("VerifyRsaSignature: Failed to parse SSH RSA public key");
				return false;
			}

			logger.LogDebug("VerifyRsaSignature: Successfully parsed RSA key. Modulus bits: {ModulusBits}",
				rsaKey.Modulus.BitLength);

			if (!TryNormalizeIncomingSignature(message, signature, out var actualSignature, out var messageBytes,
				out var signatureAlgorithm))
			{
				logger.LogDebug("VerifyRsaSignature: Failed to normalize incoming signature format");
				return false;
			}

			var hashAlgorithm = signatureAlgorithm switch
			{
				"rsa-sha2-512" => HashAlgorithmName.SHA512,
				"rsa-sha2-256" => HashAlgorithmName.SHA256,
				"ssh-rsa" => HashAlgorithmName.SHA1,
				_ => HashAlgorithmName.SHA256
			};

			if (signatureAlgorithm == "ssh-rsa")
			{
				logger.LogWarning("VerifyRsaSignature: Rejected deprecated ssh-rsa (SHA-1) signature algorithm");
				return false;
			}

			if (rsaKey.Modulus.BitLength < 2048)
			{
				logger.LogWarning("VerifyRsaSignature: Rejected RSA key with weak modulus size: {ModulusBits}", rsaKey.Modulus.BitLength);
				return false;
			}

			logger.LogDebug(
				"VerifyRsaSignature: Normalized signature. Algorithm: {SignatureAlgorithm}, Signature bytes: {SignatureLength}, Message bytes: {MessageLength}, Hash: {HashAlgorithm}",
				signatureAlgorithm,
				actualSignature.Length,
				messageBytes.Length,
				hashAlgorithm.Name);

			using var rsa = RSA.Create();
			rsa.ImportParameters(new RSAParameters
			{
				Modulus = rsaKey.Modulus.ToByteArrayUnsigned(),
				Exponent = rsaKey.Exponent.ToByteArrayUnsigned()
			});

			var verifyResult = rsa.VerifyData(messageBytes, actualSignature, hashAlgorithm,
				RSASignaturePadding.Pkcs1);
			logger.LogDebug("VerifyRsaSignature: RSA signature verification result: {Result}", verifyResult);
			return verifyResult;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "VerifyRsaSignature: Unexpected error during RSA signature verification");
			return false;
		}
	}

	private bool VerifyEd25519Signature(byte[] keyBytes, string message, string signature)
	{
		try
		{
			logger.LogDebug(
				"VerifyEd25519Signature: Starting Ed25519 signature verification. Key bytes length: {KeyBytesLength}",
				keyBytes.Length);

			var publicKey = ParseSshEd25519PublicKeyOld(keyBytes);
			if (publicKey is not { Length: 32 })
			{
				logger.LogDebug(
					"VerifyEd25519Signature: Failed to parse SSH Ed25519 public key or invalid length. Key length: {KeyLength}",
					publicKey?.Length ?? -1);
				return false;
			}

			logger.LogDebug("VerifyEd25519Signature: Successfully parsed Ed25519 public key (32 bytes)");

			if (!TryNormalizeIncomingSignature(message, signature, out var actualSignature, out var messageBytes,
				out var signatureAlgorithm))
			{
				logger.LogDebug("VerifyEd25519Signature: Failed to normalize incoming signature format");
				return false;
			}

			logger.LogDebug(
				"VerifyEd25519Signature: Normalized signature. Algorithm: {SignatureAlgorithm}, Signature bytes: {SignatureLength}, Message bytes: {MessageLength}",
				signatureAlgorithm,
				actualSignature.Length,
				messageBytes.Length);

			if (signatureAlgorithm != "ssh-ed25519")
			{
				logger.LogDebug("VerifyEd25519Signature: Unsupported signature algorithm for Ed25519 key: {SignatureAlgorithm}",
					signatureAlgorithm);
				return false;
			}

			if (actualSignature.Length != 64)
			{
				logger.LogDebug("VerifyEd25519Signature: Invalid signature length. Expected 64, got: {SignatureLength}",
					actualSignature.Length);
				return false;
			}

			var verifyResult = Ed25519.Verify(actualSignature, 0, publicKey, 0, messageBytes, 0,
				messageBytes.Length);
			logger.LogDebug("VerifyEd25519Signature: Ed25519 signature verification result: {Result}", verifyResult);

			return verifyResult;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "VerifyEd25519Signature: Unexpected error during Ed25519 signature verification");
			return false;
		}
	}

	private bool ParseSshEd25519PublicKey(byte[] keyBytes, out byte[]? publicKey)
	{
		publicKey = null;
		try
		{
			using var ms = new MemoryStream(keyBytes);
			using var reader = new BinaryReader(ms);

			var algorithmLength = ReadSshInt(reader);
			if (algorithmLength <= 0 || algorithmLength > 256)
			{
				logger.LogDebug("ParseSshEd25519PublicKey: Invalid algorithm length: {AlgorithmLength}",
					algorithmLength);
				return false;
			}

			var algorithm = Encoding.ASCII.GetString(reader.ReadBytes(algorithmLength));
			logger.LogDebug("ParseSshEd25519PublicKey: Found algorithm type: {Algorithm}", algorithm);

			if (algorithm != "ssh-ed25519")
			{
				logger.LogDebug("ParseSshEd25519PublicKey: Expected 'ssh-ed25519', found: {Algorithm}", algorithm);
				return false;
			}

			var keyLength = ReadSshInt(reader);
			if (keyLength != 32)
			{
				logger.LogDebug("ParseSshEd25519PublicKey: Invalid Ed25519 key length. Expected 32, got: {KeyLength}",
					keyLength);
				return false;
			}

			publicKey = reader.ReadBytes(keyLength);
			logger.LogDebug("ParseSshEd25519PublicKey: Successfully parsed Ed25519 public key");
			return true;
		}
		catch (Exception ex)
		{
			logger.LogDebug(ex, "ParseSshEd25519PublicKey: Error parsing Ed25519 public key");
			return false;
		}
	}

	private byte[]? ParseSshEd25519PublicKeyOld(byte[] keyBytes)
	{
		if (ParseSshEd25519PublicKey(keyBytes, out var key))
			return key;
		return null;
	}

	public string GetPublicKeyFingerprint(string publicKey)
	{
		try
		{
			var keyParts = publicKey.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (keyParts.Length < 2)
			{
				logger.LogDebug(
					"GetPublicKeyFingerprint: Invalid public key format. Expected at least 2 parts, got {PartCount}",
					keyParts.Length);
				return string.Empty;
			}

			try
			{
				var keyBytes = Convert.FromBase64String(keyParts[1]);
				var hash = SHA256.HashData(keyBytes);
				var fingerprint = $"SHA256:{Convert.ToBase64String(hash).TrimEnd('=')}";
				logger.LogDebug("GetPublicKeyFingerprint: Calculated fingerprint for {KeyType} key", keyParts[0]);
				return fingerprint;
			}
			catch (FormatException)
			{
				logger.LogDebug("GetPublicKeyFingerprint: Failed to decode base64 public key");
				return string.Empty;
			}
		}
		catch (Exception ex)
		{
			logger.LogDebug(ex, "GetPublicKeyFingerprint: Unexpected error calculating fingerprint");
			return string.Empty;
		}
	}

	public bool IsNonceUsed(string nonce)
	{
		CleanupExpiredNonces();
		var isUsed = _usedNonces.ContainsKey(nonce);
		if (isUsed)
		{
			logger.LogDebug("IsNonceUsed: Nonce has been used before");
		}

		return isUsed;
	}

	public void MarkNonceAsUsed(string nonce)
	{
		var added = _usedNonces.TryAdd(nonce, DateTime.UtcNow);
		if (added)
		{
			logger.LogDebug("MarkNonceAsUsed: Nonce marked as used");
		}
		else
		{
			logger.LogWarning("MarkNonceAsUsed: Failed to mark nonce as used (may already exist)");
		}

		CleanupExpiredNonces();
	}

	public bool TryMarkNonceAsUsed(string nonce)
	{
		CleanupExpiredNonces();
		var added = _usedNonces.TryAdd(nonce, DateTime.UtcNow);
		if (!added)
		{
			logger.LogDebug("TryMarkNonceAsUsed: Nonce has already been used");
			return false;
		}

		logger.LogDebug("TryMarkNonceAsUsed: Nonce marked as used");
		return true;
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
					if (!ParseSshEd25519PublicKey(keyBytes, out var ed25519Key) || ed25519Key is not { Length: 32 })
					{
						errorMessage = "Invalid ssh-ed25519 key format";
						return false;
					}
					return true;
				case "ssh-rsa":
					var rsaKey = ParseSshRsaPublicKey(keyBytes);
					if (rsaKey == null)
					{
						errorMessage = "Invalid ssh-rsa key format";
						return false;
					}
					if (rsaKey.Modulus.BitLength < 2048)
					{
						errorMessage = "RSA keys must be at least 2048 bits";
						return false;
					}
					return true;
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
	private void CleanupExpiredNonces()
	{
		if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromMinutes(1))
			return;

		_lastCleanup = DateTime.UtcNow;
		var expiredKeys = _usedNonces
			.Where(kvp => DateTime.UtcNow - kvp.Value > _nonceExpiration)
			.Select(kvp => kvp.Key)
			.ToList();

		if (expiredKeys.Count > 0)
		{
			logger.LogDebug(
				"CleanupExpiredNonces: Removing {ExpiredNonceCount} expired nonces. Total nonces before cleanup: {TotalNoncesBefore}",
				expiredKeys.Count, _usedNonces.Count);
		}

		foreach (var key in expiredKeys)
			_usedNonces.TryRemove(key, out _);

		if (expiredKeys.Count > 0)
		{
			logger.LogDebug("CleanupExpiredNonces: Cleanup completed. Total nonces after cleanup: {TotalNoncesAfter}",
				_usedNonces.Count);
		}
	}

	private RsaKeyParameters? ParseSshRsaPublicKey(byte[] keyBytes)
	{
		try
		{
			using var ms = new MemoryStream(keyBytes);
			using var reader = new BinaryReader(ms);

			var algorithmLength = ReadSshInt(reader);
			if (algorithmLength <= 0 || algorithmLength > 256)
			{
				logger.LogDebug("ParseSshRsaPublicKey: Invalid algorithm length: {AlgorithmLength}", algorithmLength);
				return null;
			}

			var algorithm = Encoding.ASCII.GetString(reader.ReadBytes(algorithmLength));
			logger.LogDebug("ParseSshRsaPublicKey: Found algorithm type: {Algorithm}", algorithm);

			if (algorithm != "ssh-rsa")
			{
				logger.LogDebug("ParseSshRsaPublicKey: Expected 'ssh-rsa', found: {Algorithm}", algorithm);
				return null;
			}

			var exponentLength = ReadSshInt(reader);
			if (exponentLength <= 0 || exponentLength > 8192)
			{
				logger.LogDebug("ParseSshRsaPublicKey: Invalid exponent length: {ExponentLength}", exponentLength);
				return null;
			}

			var exponent = new Org.BouncyCastle.Math.BigInteger(1, reader.ReadBytes(exponentLength));
			logger.LogDebug("ParseSshRsaPublicKey: Parsed exponent, exponent bits: {ExponentBits}",
				exponent.BitLength);

			var modulusLength = ReadSshInt(reader);
			if (modulusLength <= 0 || modulusLength > 8192)
			{
				logger.LogDebug("ParseSshRsaPublicKey: Invalid modulus length: {ModulusLength}", modulusLength);
				return null;
			}

			var modulus = new Org.BouncyCastle.Math.BigInteger(1, reader.ReadBytes(modulusLength));
			logger.LogDebug("ParseSshRsaPublicKey: Successfully parsed RSA key. Modulus bits: {ModulusBits}",
				modulus.BitLength);

			return new RsaKeyParameters(false, modulus, exponent);
		}
		catch (Exception ex)
		{
			logger.LogDebug(ex, "ParseSshRsaPublicKey: Error parsing RSA public key");
			return null;
		}
	}

	private byte[]? ExtractSignatureFromSshFormat(byte[] signatureBytes)
	{
		try
		{
			using var ms = new MemoryStream(signatureBytes);
			using var reader = new BinaryReader(ms);

			var algorithmLength = ReadSshInt(reader);
			if (algorithmLength is <= 0 or > 256)
			{
				logger.LogDebug("ExtractSignatureFromSshFormat: Invalid algorithm length: {AlgorithmLength}",
					algorithmLength);
				return null;
			}

			var algorithm = Encoding.ASCII.GetString(reader.ReadBytes(algorithmLength));
			logger.LogDebug("ExtractSignatureFromSshFormat: Signature algorithm: {Algorithm}", algorithm);

			var sigLength = ReadSshInt(reader);
			if (sigLength is <= 0 or > 8192)
			{
				logger.LogDebug("ExtractSignatureFromSshFormat: Invalid signature length: {SignatureLength}",
					sigLength);
				return null;
			}

			var sig = reader.ReadBytes(sigLength);
			logger.LogDebug(
				"ExtractSignatureFromSshFormat: Successfully extracted signature of length {SignatureLength}",
				sigLength);
			return sig;
		}
		catch (Exception ex)
		{
			logger.LogDebug(ex, "ExtractSignatureFromSshFormat: Error extracting signature from SSH format");
			return null;
		}
	}

	private bool TryNormalizeIncomingSignature(string message, string signatureBase64, out byte[] actualSignature,
		out byte[] messageBytesToVerify, out string signatureAlgorithm)
	{
		actualSignature = Array.Empty<byte>();
		messageBytesToVerify = Array.Empty<byte>();
		signatureAlgorithm = string.Empty;

		byte[] decoded;
		try
		{
			decoded = Convert.FromBase64String(signatureBase64);
			logger.LogDebug("TryNormalizeIncomingSignature: Decoded signature length: {DecodedLength}", decoded.Length);
		}
		catch (FormatException)
		{
			logger.LogDebug("TryNormalizeIncomingSignature: Failed to decode base64 signature");
			return false;
		}

		if (TryExtractFromSshSigEnvelope(decoded, message, out actualSignature, out messageBytesToVerify,
			out signatureAlgorithm))
		{
			logger.LogDebug("TryNormalizeIncomingSignature: Parsed OpenSSH SSHSIG envelope");
			return true;
		}

		if (TryExtractSshSignatureBlob(decoded, out signatureAlgorithm, out actualSignature))
		{
			messageBytesToVerify = Encoding.UTF8.GetBytes(message);
			logger.LogDebug("TryNormalizeIncomingSignature: Parsed SSH signature blob format");
			return true;
		}

		signatureAlgorithm = InferRawSignatureAlgorithmFromLength(decoded.Length);
		actualSignature = decoded;
		messageBytesToVerify = Encoding.UTF8.GetBytes(message);
		logger.LogDebug("TryNormalizeIncomingSignature: Using raw signature fallback. Inferred algorithm: {SignatureAlgorithm}",
			signatureAlgorithm);
		return true;
	}

	private bool TryExtractFromSshSigEnvelope(byte[] decodedSignature, string message, out byte[] actualSignature,
		out byte[] messageBytesToVerify, out string signatureAlgorithm)
	{
		actualSignature = Array.Empty<byte>();
		messageBytesToVerify = Array.Empty<byte>();
		signatureAlgorithm = string.Empty;

		try
		{
			if (decodedSignature.Length < SshSigMagic.Length)
				return false;

			if (!decodedSignature.AsSpan(0, SshSigMagic.Length).SequenceEqual(SshSigMagic))
				return false;

			using var ms = new MemoryStream(decodedSignature);
			using var reader = new BinaryReader(ms);

			var magic = reader.ReadBytes(SshSigMagic.Length);
			if (!magic.AsSpan().SequenceEqual(SshSigMagic))
				return false;

			var version = ReadUint32BigEndian(reader);
			if (version != 1)
			{
				logger.LogDebug("TryExtractFromSshSigEnvelope: Unsupported SSHSIG version: {Version}", version);
				return false;
			}

			_ = ReadSshStringBytes(reader);
			var sigNamespace = ReadSshString(reader);
			var reserved = ReadSshStringBytes(reader);
			var hashAlgorithm = ReadSshString(reader);
			var signatureBlob = ReadSshStringBytes(reader);

			if (!TryExtractSshSignatureBlob(signatureBlob, out signatureAlgorithm, out actualSignature))
			{
				logger.LogDebug("TryExtractFromSshSigEnvelope: Failed to parse nested SSH signature blob");
				return false;
			}

			var messageHash = ComputeHashForSshSig(message, hashAlgorithm);
			if (messageHash == null)
			{
				logger.LogDebug("TryExtractFromSshSigEnvelope: Unsupported SSHSIG hash algorithm: {HashAlgorithm}",
					hashAlgorithm);
				return false;
			}

			messageBytesToVerify = BuildSshSigSignedData(sigNamespace, reserved, hashAlgorithm, messageHash);
			logger.LogDebug(
				"TryExtractFromSshSigEnvelope: Parsed SSHSIG. Namespace: {Namespace}, HashAlgorithm: {HashAlgorithm}, NestedSignatureAlgorithm: {SignatureAlgorithm}, NestedSignatureLength: {SignatureLength}",
				sigNamespace,
				hashAlgorithm,
				signatureAlgorithm,
				actualSignature.Length);

			return true;
		}
		catch (Exception ex)
		{
			logger.LogDebug(ex, "TryExtractFromSshSigEnvelope: Failed to parse SSHSIG envelope");
			return false;
		}
	}

	private bool TryExtractSshSignatureBlob(byte[] blob, out string algorithm, out byte[] signature)
	{
		algorithm = string.Empty;
		signature = Array.Empty<byte>();

		try
		{
			using var ms = new MemoryStream(blob);
			using var reader = new BinaryReader(ms);

			algorithm = ReadSshString(reader);
			signature = ReadSshStringBytes(reader);

			if (string.IsNullOrWhiteSpace(algorithm) || signature.Length == 0)
				return false;

			return true;
		}
		catch
		{
			return false;
		}
	}

	private static string InferRawSignatureAlgorithmFromLength(int signatureLength)
	{
		if (signatureLength == 64)
			return "ssh-ed25519";

		if (signatureLength >= 256)
			return "rsa-sha2-256";

		return "raw-unknown";
	}

	private static byte[]? ComputeHashForSshSig(string message, string hashAlgorithm)
	{
		var messageBytes = Encoding.UTF8.GetBytes(message);
		return hashAlgorithm switch
		{
			"sha256" => SHA256.HashData(messageBytes),
			"sha512" => SHA512.HashData(messageBytes),
			_ => null
		};
	}

	private static byte[] BuildSshSigSignedData(string sigNamespace, byte[] reserved, string hashAlgorithm,
		byte[] messageHash)
	{
		using var ms = new MemoryStream();
		using var writer = new BinaryWriter(ms);

		writer.Write(SshSigMagic);
		WriteSshString(writer, sigNamespace);
		WriteSshString(writer, reserved);
		WriteSshString(writer, hashAlgorithm);
		WriteSshString(writer, messageHash);

		return ms.ToArray();
	}

	private string ReadSshString(BinaryReader reader)
	{
		var bytes = ReadSshStringBytes(reader);
		return Encoding.UTF8.GetString(bytes);
	}

	private byte[] ReadSshStringBytes(BinaryReader reader)
	{
		var length = ReadSshInt(reader);
		if (length < 0)
			throw new InvalidDataException("Invalid SSH string length");

		var bytes = reader.ReadBytes(length);
		if (bytes.Length != length)
			throw new EndOfStreamException("Unexpected end of SSH string data");

		return bytes;
	}

	private static void WriteSshString(BinaryWriter writer, string value)
	{
		WriteSshString(writer, Encoding.UTF8.GetBytes(value));
	}

	private static void WriteSshString(BinaryWriter writer, byte[] value)
	{
		var lengthBytes = BitConverter.GetBytes(value.Length);
		if (BitConverter.IsLittleEndian)
			Array.Reverse(lengthBytes);

		writer.Write(lengthBytes);
		writer.Write(value);
	}

	private static uint ReadUint32BigEndian(BinaryReader reader)
	{
		var bytes = reader.ReadBytes(4);
		if (bytes.Length != 4)
			throw new EndOfStreamException("Unexpected end of uint32 data");

		if (BitConverter.IsLittleEndian)
			Array.Reverse(bytes);

		return BitConverter.ToUInt32(bytes, 0);
	}

	private int ReadSshInt(BinaryReader reader)
	{
		var bytes = reader.ReadBytes(4);
		if (BitConverter.IsLittleEndian)
			Array.Reverse(bytes);
		var value = BitConverter.ToInt32(bytes, 0);

		if (value is < 0 or > 1_000_000)
		{
			logger.LogDebug("ReadSshInt: Int value out of valid range: {Value}", value);
			return -1;
		}

		return value;
	}

	private bool WarnInvalidKeyType(string keyType)
	{
		logger.LogWarning("VerifySignature: Unsupported key type: {KeyType}", keyType);
		return false;
	}
}
