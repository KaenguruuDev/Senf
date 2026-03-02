using Microsoft.AspNetCore.DataProtection;

namespace Senf.Services;

public interface IEncryptionService
{
	string Encrypt(string plainText);
	string Decrypt(string encryptedText);
}

public class EncryptionService(IDataProtectionProvider dataProtectionProvider) : IEncryptionService
{
	private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("Senf.EnvFileProtector");

	public string Encrypt(string plainText)
		=> string.IsNullOrEmpty(plainText) ? string.Empty : _protector.Protect(plainText);

	public string Decrypt(string encryptedText)
		=> string.IsNullOrEmpty(encryptedText) ? string.Empty : _protector.Unprotect(encryptedText);
}