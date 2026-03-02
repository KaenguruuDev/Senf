namespace Senf.Dtos;

// Request DTOs
public class EnvFileUpdateRequest
{
    public string Content { get; set; } = string.Empty;
}

public class SshKeyCreateRequest
{
    public string PublicKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class SshKeyUpdateRequest
{
    public string Name { get; set; } = string.Empty;
}

// Error Response
public class ErrorResponse
{
    public bool Success { get; } = false;
    public string Error { get; set; } = string.Empty;
}

// Data Response DTOs
public class EnvFileResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int LastUpdatedByKeyId { get; set; }
}

public class SshKeyResponse
{
    public int Id { get; set; }
    public string PublicKey { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class SshKeysListResponse
{
    public List<SshKeyResponse> Keys { get; set; } = [];
}
