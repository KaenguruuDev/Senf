using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Senf.Services;

public class AdminConfig
{
    public List<AdminUserEntry> Admins { get; set; } = [];
}

public class AdminUserEntry
{
    public string Username { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public int? UserId { get; set; }
}

public interface IAdminConfigProvider
{
    AdminConfig GetConfig();
    string ConfigPath { get; }
}

public class AdminConfigProvider : IAdminConfigProvider
{
    private readonly string _configPath;
    private readonly ILogger<AdminConfigProvider> _logger;
    private readonly object _lock = new();
    private AdminConfig? _cached;

    public AdminConfigProvider(string configPath, ILogger<AdminConfigProvider> logger)
    {
        _configPath = configPath;
        _logger = logger;
    }

    public string ConfigPath => _configPath;

    public AdminConfig GetConfig()
    {
        lock (_lock)
        {
            _cached ??= LoadConfig();
            return _cached;
        }
    }

    private AdminConfig LoadConfig()
    {
        if (!File.Exists(_configPath))
        {
            _logger.LogWarning("Admin config file not found at {Path}", _configPath);
            return new AdminConfig();
        }

        try
        {
            var yaml = File.ReadAllText(_configPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var config = deserializer.Deserialize<AdminConfig>(yaml) ?? new AdminConfig();
            config.Admins ??= [];
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse admin config at {Path}", _configPath);
            return new AdminConfig();
        }
    }
}
