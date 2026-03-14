using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Senf.Authentication;
using Senf.Data;
using Senf.Routes;
using Senf.Services;

namespace Senf;

public static class Program
{
	public static async Task Main(string[] args)
	{
		var (debugEnabled, appArgs) = ParseCliFlags(args);
		if (appArgs.Length > 0 && appArgs[0] == "run")
			appArgs = appArgs[1..];
		var builder = WebApplication.CreateBuilder(appArgs);

		builder.Logging.ClearProviders();
		builder.Logging.AddConsole();
		builder.Logging.SetMinimumLevel(debugEnabled ? LogLevel.Debug : LogLevel.Information);

		string dbPath;
		var dbPathEnv = Environment.GetEnvironmentVariable("DATABASE_PATH");

		if (!string.IsNullOrEmpty(dbPathEnv))
		{
			dbPath = dbPathEnv;
			Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
		}
		else
		{
			var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			var dbDirectory = Path.Combine(appDataPath, "Senf");
			Directory.CreateDirectory(dbDirectory);
			dbPath = Path.Combine(dbDirectory, "senf.db");
		}

		var dataProtectionKeysPath = Environment.GetEnvironmentVariable("DATA_PROTECTION_KEYS_PATH");
		if (string.IsNullOrEmpty(dataProtectionKeysPath))
		{
			dataProtectionKeysPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "DataProtection-Keys");
		}

		Directory.CreateDirectory(dataProtectionKeysPath);

		builder.Services.AddDbContext<AppDbContext>(options =>
			options.UseSqlite($"Data Source={dbPath}"));

		builder.Services.AddDataProtection()
			.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
			.SetApplicationName("Senf");

		builder.Services.AddScoped<IEncryptionService, EncryptionService>();
		builder.Services.AddScoped<IUserManagementService, UserManagementService>();
		builder.Services.AddScoped<IEnvFileService, EnvFileService>();
		builder.Services.AddSingleton<ISshAuthService, SshAuthService>();
		builder.Services.AddSingleton<IAdminAuthorizationService, AdminAuthorizationService>();
		builder.Services.AddSingleton<IAdminConfigProvider>(sp =>
		{
			var configPath = Environment.GetEnvironmentVariable("ADMIN_CONFIG_PATH");
			if (string.IsNullOrWhiteSpace(configPath))
				configPath = Path.Combine(Directory.GetCurrentDirectory(), "config.yml");

			return new AdminConfigProvider(
				configPath,
				sp.GetRequiredService<ILogger<AdminConfigProvider>>());
		});
		builder.Services.AddScoped<IInviteService, InviteService>();
		builder.Services.AddAuthentication(SshAuthenticationOptions.DefaultScheme)
			.AddScheme<SshAuthenticationOptions, SshAuthenticationHandler>(
				SshAuthenticationOptions.DefaultScheme, options => { });

		builder.Services.AddAuthorization();
		builder.Services.AddOpenApi();

		var app = builder.Build();

		var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
		if (!int.TryParse(port, out var portNumber) || portNumber < 1 || portNumber > 65535)
			portNumber = 5000;
		
		app.Urls.Add($"http://+:{portNumber}");

		using (var scope = app.Services.CreateScope())
		{
			var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			await dbContext.Database.MigrateAsync();

			var adminConfigProvider = scope.ServiceProvider.GetRequiredService<IAdminConfigProvider>();
			var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
			var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
			var logger = loggerFactory.CreateLogger("AdminConfig");
			var adminConfig = adminConfigProvider.GetConfig();

			foreach (var admin in adminConfig.Admins)
			{
				if (string.IsNullOrWhiteSpace(admin.Username) || string.IsNullOrWhiteSpace(admin.PublicKey))
				{
					logger.LogWarning(
						"Skipping admin entry with missing username or public key in {Path}",
						adminConfigProvider.ConfigPath);
					continue;
				}

				var exists = await dbContext.Users
					.AnyAsync(u => u.Username == admin.Username);
				if (exists)
					continue;

				var (success, message) = await userManagementService
					.CreateUserAsync(admin.Username, admin.PublicKey, "default");

				if (!success)
					logger.LogWarning("Failed to seed admin '{Username}': {Message}", admin.Username, message);
			}
		}

		if (app.Environment.IsDevelopment())
			app.MapOpenApi();

		app.UseHttpsRedirection();
		app.UseAuthentication();
		app.UseAuthorization();

		app.MapHealthRoutes();
		app.MapEnvFileRoutes();
		app.MapSshKeyRoutes();
		app.MapUserRoutes();
		app.MapSharingRoutes();
		app.MapInviteRoutes();

		await app.RunAsync();
	}

	private static (bool DebugEnabled, string[] RemainingArgs) ParseCliFlags(string[] args)
	{
		var debugEnabled = false;
		var remainingArgs = new List<string>(args.Length);

		foreach (var arg in args)
		{
			if (arg is "--debug" or "-d")
			{
				debugEnabled = true;
				continue;
			}

			remainingArgs.Add(arg);
		}

		return (debugEnabled, remainingArgs.ToArray());
	}
}
