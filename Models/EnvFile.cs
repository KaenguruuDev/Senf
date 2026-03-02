namespace Senf.Models;

public class EnvFile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public string EncryptedContent { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int LastUpdatedBySshKeyId { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;
}
