namespace Senf.Models;

public class Share
{
	public int Id { get; set; }

	public int EnvFileId { get; set; }
	public EnvFile EnvFile { get; set; } = null!;

	public int SharedToUserId { get; set; }
	public User SharedToUser { get; set; } = null!;

	public ShareMode ShareMode { get; set; }

	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
}

public enum ShareMode
{
	ReadOnly,
	ReadWrite
}