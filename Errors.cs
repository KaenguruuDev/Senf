using Microsoft.AspNetCore.Http;

namespace Senf.Dtos;

public abstract record ApiError(string Code);

public sealed record EnvFileError(string Code) : ApiError(Code);

public sealed record SshKeyError(string Code) : ApiError(Code);

public sealed record SharingError(string Code) : ApiError(Code);

public sealed record ErrorDefinition(string Code, int Status, string Title, string Detail);

public static class EnvFileErrors
{
    public const string NameRequiredCode = "env_file.name_required";
    public const string ContentRequiredCode = "env_file.content_required";
    public const string NotFoundCode = "env_file.not_found";
    public const string AlreadyExistsCode = "env_file.already_exists";

    public static EnvFileError NameRequired => new(NameRequiredCode);
    public static EnvFileError ContentRequired => new(ContentRequiredCode);
    public static EnvFileError NotFound => new(NotFoundCode);
    public static EnvFileError AlreadyExists => new(AlreadyExistsCode);
}

public static class SshKeyErrors
{
    public const string PublicKeyRequiredCode = "ssh_key.public_key_required";
    public const string NameRequiredCode = "ssh_key.name_required";
    public const string KeyIdRequiredCode = "ssh_key.key_id_required";
    public const string InvalidKeyFormatCode = "ssh_key.invalid_key_format";
    public const string UnsupportedKeyTypeCode = "ssh_key.unsupported_key_type";
    public const string RsaKeyTooSmallCode = "ssh_key.rsa_key_too_small";
    public const string DuplicateFingerprintCode = "ssh_key.duplicate_fingerprint";
    public const string DuplicateNameCode = "ssh_key.duplicate_name";
    public const string NotFoundCode = "ssh_key.not_found";
    public const string UserNotFoundCode = "ssh_key.user_not_found";
    public const string CannotDeleteLastKeyCode = "ssh_key.cannot_delete_last_key";

    public static SshKeyError PublicKeyRequired => new(PublicKeyRequiredCode);
    public static SshKeyError NameRequired => new(NameRequiredCode);
    public static SshKeyError KeyIdRequired => new(KeyIdRequiredCode);
    public static SshKeyError InvalidKeyFormat => new(InvalidKeyFormatCode);
    public static SshKeyError UnsupportedKeyType => new(UnsupportedKeyTypeCode);
    public static SshKeyError RsaKeyTooSmall => new(RsaKeyTooSmallCode);
    public static SshKeyError DuplicateFingerprint => new(DuplicateFingerprintCode);
    public static SshKeyError DuplicateName => new(DuplicateNameCode);
    public static SshKeyError NotFound => new(NotFoundCode);
    public static SshKeyError UserNotFound => new(UserNotFoundCode);
    public static SshKeyError CannotDeleteLastKey => new(CannotDeleteLastKeyCode);
}

public static class SharingErrors
{
    public const string ShareIdRequiredCode = "share.share_id_required";
    public const string EnvFileIdRequiredCode = "share.env_file_id_required";
    public const string ShareToUserIdRequiredCode = "share.share_to_user_id_required";
    public const string ShareToUserIdInvalidCode = "share.share_to_user_id_invalid";
    public const string ShareModeInvalidCode = "share.share_mode_invalid";
    public const string ContentRequiredCode = "share.content_required";
    public const string CannotShareToSelfCode = "share.cannot_share_to_self";
    public const string EnvFileNotFoundCode = "share.env_file_not_found";
    public const string ShareToUserNotFoundCode = "share.share_to_user_not_found";
    public const string ShareNotFoundCode = "share.not_found";
    public const string ReadOnlyCode = "share.read_only";
    public const string ConflictCode = "share.conflict";

    public static SharingError ShareIdRequired => new(ShareIdRequiredCode);
    public static SharingError EnvFileIdRequired => new(EnvFileIdRequiredCode);
    public static SharingError ShareToUserIdRequired => new(ShareToUserIdRequiredCode);
    public static SharingError ShareToUserIdInvalid => new(ShareToUserIdInvalidCode);
    public static SharingError ShareModeInvalid => new(ShareModeInvalidCode);
    public static SharingError ContentRequired => new(ContentRequiredCode);
    public static SharingError CannotShareToSelf => new(CannotShareToSelfCode);
    public static SharingError EnvFileNotFound => new(EnvFileNotFoundCode);
    public static SharingError ShareToUserNotFound => new(ShareToUserNotFoundCode);
    public static SharingError ShareNotFound => new(ShareNotFoundCode);
    public static SharingError ReadOnly => new(ReadOnlyCode);
    public static SharingError Conflict => new(ConflictCode);
}

public static class ApiErrorCatalog
{
    public const string UnexpectedCode = "server.unexpected";

    public static ErrorDefinition Unexpected => new(
        UnexpectedCode,
        StatusCodes.Status500InternalServerError,
        "Unexpected error",
        "An unexpected error occurred.");

    public static ErrorDefinition ToDefinition(this ApiError error) => error.Code switch
    {
        EnvFileErrors.NameRequiredCode => new(
            EnvFileErrors.NameRequiredCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "Environment file name is required."),
        EnvFileErrors.ContentRequiredCode => new(
            EnvFileErrors.ContentRequiredCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "Environment file content is required."),
        EnvFileErrors.NotFoundCode => new(
            EnvFileErrors.NotFoundCode,
            StatusCodes.Status404NotFound,
            "Not found",
            "Environment file not found."),
        EnvFileErrors.AlreadyExistsCode => new(
            EnvFileErrors.AlreadyExistsCode,
            StatusCodes.Status409Conflict,
            "Conflict",
            "Environment file already exists."),

        SshKeyErrors.PublicKeyRequiredCode => new(
            SshKeyErrors.PublicKeyRequiredCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "SSH public key cannot be empty."),
        SshKeyErrors.NameRequiredCode => new(
            SshKeyErrors.NameRequiredCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "Key name cannot be empty."),
        SshKeyErrors.KeyIdRequiredCode => new(
            SshKeyErrors.KeyIdRequiredCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "Key ID is required."),
        SshKeyErrors.InvalidKeyFormatCode => new(
            SshKeyErrors.InvalidKeyFormatCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "Invalid SSH key format."),
        SshKeyErrors.UnsupportedKeyTypeCode => new(
            SshKeyErrors.UnsupportedKeyTypeCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "Unsupported SSH key type. Supported types: ssh-rsa, ssh-ed25519."),
        SshKeyErrors.RsaKeyTooSmallCode => new(
            SshKeyErrors.RsaKeyTooSmallCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "RSA keys must be at least 2048 bits."),
        SshKeyErrors.DuplicateFingerprintCode => new(
            SshKeyErrors.DuplicateFingerprintCode,
            StatusCodes.Status409Conflict,
            "Conflict",
            "SSH key with this fingerprint already exists."),
        SshKeyErrors.DuplicateNameCode => new(
            SshKeyErrors.DuplicateNameCode,
            StatusCodes.Status409Conflict,
            "Conflict",
            "SSH key name is already in use."),
        SshKeyErrors.NotFoundCode => new(
            SshKeyErrors.NotFoundCode,
            StatusCodes.Status404NotFound,
            "Not found",
            "SSH key not found."),
        SshKeyErrors.UserNotFoundCode => new(
            SshKeyErrors.UserNotFoundCode,
            StatusCodes.Status404NotFound,
            "Not found",
            "User not found."),
        SshKeyErrors.CannotDeleteLastKeyCode => new(
            SshKeyErrors.CannotDeleteLastKeyCode,
            StatusCodes.Status409Conflict,
            "Conflict",
            "Cannot delete the last SSH key. At least one key must remain."),

        SharingErrors.ShareIdRequiredCode => new(
            SharingErrors.ShareIdRequiredCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "Share ID is required."),
        SharingErrors.EnvFileIdRequiredCode => new(
            SharingErrors.EnvFileIdRequiredCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "Environment file ID is required."),
        SharingErrors.ShareToUserIdRequiredCode => new(
            SharingErrors.ShareToUserIdRequiredCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "Share-to user ID is required."),
        SharingErrors.ShareToUserIdInvalidCode => new(
            SharingErrors.ShareToUserIdInvalidCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "Share-to user ID is invalid."),
        SharingErrors.ShareModeInvalidCode => new(
            SharingErrors.ShareModeInvalidCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "Share mode is invalid."),
        SharingErrors.ContentRequiredCode => new(
            SharingErrors.ContentRequiredCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "Environment file content is required."),
        SharingErrors.CannotShareToSelfCode => new(
            SharingErrors.CannotShareToSelfCode,
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "Cannot share to the same user."),
        SharingErrors.EnvFileNotFoundCode => new(
            SharingErrors.EnvFileNotFoundCode,
            StatusCodes.Status404NotFound,
            "Not found",
            "Environment file not found."),
        SharingErrors.ShareToUserNotFoundCode => new(
            SharingErrors.ShareToUserNotFoundCode,
            StatusCodes.Status404NotFound,
            "Not found",
            "Share-to user not found."),
        SharingErrors.ShareNotFoundCode => new(
            SharingErrors.ShareNotFoundCode,
            StatusCodes.Status404NotFound,
            "Not found",
            "Share not found."),
        SharingErrors.ReadOnlyCode => new(
            SharingErrors.ReadOnlyCode,
            StatusCodes.Status403Forbidden,
            "Forbidden",
            "Share is read-only."),
        SharingErrors.ConflictCode => new(
            SharingErrors.ConflictCode,
            StatusCodes.Status409Conflict,
            "Conflict",
            "Share already exists."),

        _ => Unexpected
    };
}

public static class ApiProblem
{
    public static IResult FromError(ApiError error, HttpContext context)
    {
        var def = error.ToDefinition();
        return Results.Problem(
            statusCode: def.Status,
            title: def.Title,
            detail: def.Detail,
            type: BuildType(def.Code),
            instance: context.Request.Path,
            extensions: BuildExtensions(def.Code, context));
    }

    public static IResult Validation(ApiError error, HttpContext context, string field)
    {
        var def = error.ToDefinition();
        var errors = new Dictionary<string, string[]>
        {
            [field] = new[] { def.Detail }
        };

        return Results.ValidationProblem(
            errors,
            statusCode: def.Status,
            title: def.Title,
            detail: def.Detail,
            type: BuildType(def.Code),
            instance: context.Request.Path,
            extensions: BuildExtensions(def.Code, context));
    }

    public static IResult Unexpected(HttpContext context)
        => Results.Problem(
            statusCode: ApiErrorCatalog.Unexpected.Status,
            title: ApiErrorCatalog.Unexpected.Title,
            detail: ApiErrorCatalog.Unexpected.Detail,
            type: BuildType(ApiErrorCatalog.Unexpected.Code),
            instance: context.Request.Path,
            extensions: BuildExtensions(ApiErrorCatalog.Unexpected.Code, context));

    private static string BuildType(string code)
    {
        var baseValue = Environment.GetEnvironmentVariable("API_PROBLEM_TYPE_BASE");
        if (string.IsNullOrWhiteSpace(baseValue))
            return $"urn:senf:error:{code}";

        if (baseValue.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
            return $"{baseValue.TrimEnd(':')}:{code}";

        return $"{baseValue.TrimEnd('/')}/{code}";
    }

    private static Dictionary<string, object?> BuildExtensions(string code, HttpContext context)
        => new()
        {
            ["code"] = code,
            ["traceId"] = context.TraceIdentifier
        };
}

public static class SshKeyErrorMessages
{
    public static string ToMessage(this SshKeyError error) => error.Code switch
    {
        SshKeyErrors.PublicKeyRequiredCode => "SSH public key cannot be empty",
        SshKeyErrors.NameRequiredCode => "Key name cannot be empty",
        SshKeyErrors.KeyIdRequiredCode => "Key ID is required",
        SshKeyErrors.InvalidKeyFormatCode => "Invalid SSH key format",
        SshKeyErrors.UnsupportedKeyTypeCode => "Unsupported SSH key type. Supported types: ssh-rsa, ssh-ed25519",
        SshKeyErrors.RsaKeyTooSmallCode => "RSA keys must be at least 2048 bits",
        SshKeyErrors.DuplicateFingerprintCode => "SSH key with this fingerprint already exists",
        SshKeyErrors.DuplicateNameCode => "SSH key name is already in use",
        SshKeyErrors.NotFoundCode => "SSH key not found",
        SshKeyErrors.UserNotFoundCode => "User not found",
        SshKeyErrors.CannotDeleteLastKeyCode => "Cannot delete the last SSH key. At least one key must remain.",
        _ => "Unknown SSH key error"
    };
}
