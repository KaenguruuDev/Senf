namespace Senf.Models;

public class SshKey
{
    public int Id { get; set; }
    public string PublicKey { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;
}
