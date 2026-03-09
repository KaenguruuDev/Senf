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

public class ShareEnvFileRequest
{
    public int EnvFileId { get; set; }
    public int ShareToUserId { get; set; }
    public ShareModeDto ShareMode { get; set; }
}

public class RemoveEnvShareRequest
{
    public int EnvFileId { get; set; }
    public int? ShareToUserId { get; set; }
}

public class ShareResponse
{
    public int Id { get; set; }
    public int EnvFileId { get; set; }
    public string EnvFileName { get; set; } = string.Empty;
    public int SharedToUserId { get; set; }
    public string SharedToUsername { get; set; } = string.Empty;
    public ShareModeDto ShareMode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SharesListResponse
{
    public List<ShareResponse> Shares { get; set; } = [];
}

public class SharedEnvFileResponse
{
    public int ShareId { get; set; }
    public int EnvFileId { get; set; }
    public string EnvFileName { get; set; } = string.Empty;
    public int OwnerUserId { get; set; }
    public string OwnerUsername { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int LastUpdatedByKeyId { get; set; }
    public ShareModeDto ShareMode { get; set; }
    public DateTime SharedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SharedEnvFilesListResponse
{
    public List<SharedEnvFileResponse> Files { get; set; } = [];
}

public enum ShareModeDto
{
    ReadOnly,
    ReadWrite
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

public class UserSummaryResponse
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
}

public class UsersListResponse
{
    public List<UserSummaryResponse> Users { get; set; } = [];
}