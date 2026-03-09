using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Senf.Services;

public interface ISshAuthService
{
	bool VerifySignature(string publicKey, string message, string signature);
	string GetPublicKeyFingerprint(string publicKey);
	bool IsSupportedPublicKey(string publicKey);

	bool IsNonceUsed(string nonce);
	void MarkNonceAsUsed(string nonce);
	bool TryMarkNonceAsUsed(string nonce);
}

public class SshAuthService() : ISshAuthService
{
	private readonly ConcurrentDictionary<string, DateTime> _usedNonces = new();
	private readonly TimeSpan _nonceExpiration = TimeSpan.FromMinutes(10);
	private DateTime _lastCleanup = DateTime.UtcNow;

	public bool VerifySignature(string publicKey, string message, string signature)
	{
		var messageBytes = Encoding.UTF8.GetBytes(message);
		var pubKey = ParseSshEd25519(publicKey);
		var byteSignature = Convert.FromBase64String(signature);

		return pubKey is not null && Verify(pubKey, messageBytes, byteSignature);
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

	public bool IsSupportedPublicKey(string publicKey)
		=> ParseSshEd25519(publicKey) != null;

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

	private static Ed25519PublicKeyParameters? ParseSshEd25519(string sshKey)
	{
		try
		{
			var parts = sshKey.Split(' ', StringSplitOptions.RemoveEmptyEntries);

			var keyBlob = Convert.FromBase64String(parts[1]);

			using var ms = new MemoryStream(keyBlob);
			using var br = new BinaryReader(ms);

			var typeLen = ReadInt();
			var type = Encoding.ASCII.GetString(br.ReadBytes(typeLen));

			if (type != "ssh-ed25519")
				return null;

			var keyLen = ReadInt();
			var keyBytes = br.ReadBytes(keyLen);

			return new Ed25519PublicKeyParameters(keyBytes, 0);

			int ReadInt() => BinaryPrimitives.ReadInt32BigEndian(br.ReadBytes(4));
		}
		catch
		{
			return null;
		}
	}

	private static bool Verify(
		Ed25519PublicKeyParameters publicKey,
		byte[] message,
		byte[] signature)
	{
		var verifier = new Ed25519Signer();
		verifier.Init(false, publicKey);
		verifier.BlockUpdate(message, 0, message.Length);

		return verifier.VerifySignature(signature);
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
}