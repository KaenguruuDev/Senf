namespace Senf.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public ICollection<EnvFile> EnvFiles { get; set; } = new List<EnvFile>();
    public ICollection<SshKey> SshKeys { get; set; } = new List<SshKey>();
}
