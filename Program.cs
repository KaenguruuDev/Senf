using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Senf.Authentication;
using Senf.Data;
using Senf.Routes;
using Senf.Services;
using Senf.Dtos;

namespace Senf;

public static class Program
{
	public static async Task Main(string[] args)
	{
		var (debugEnabled, appArgs) = ParseCliFlags(args);
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
		builder.Services.AddAuthentication(SshAuthenticationOptions.DefaultScheme)
			.AddScheme<SshAuthenticationOptions, SshAuthenticationHandler>(
				SshAuthenticationOptions.DefaultScheme, options => { });

		builder.Services.AddAuthorization();
		builder.Services.AddOpenApi();

		var app = builder.Build();

		var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
		if (!int.TryParse(port, out var portNumber) || portNumber < 1 || portNumber > 65535)
		{
			portNumber = 5000;
		}

		app.Urls.Clear();
		app.Urls.Add($"http://+:{portNumber}");

		using (var scope = app.Services.CreateScope())
		{
			var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			await dbContext.Database.EnsureCreatedAsync();
		}

		if (appArgs.Length > 0 && appArgs[0] != "run")
		{
			await HandleCliCommand(appArgs, app.Services);
			return;
		}

		if (app.Environment.IsDevelopment())
			app.MapOpenApi();

		app.UseHttpsRedirection();
		app.UseAuthentication();
		app.UseAuthorization();

		app.MapHealthRoutes();
		app.MapEnvFileRoutes();
		app.MapSshKeyRoutes();

		await app.RunAsync();
	}

	private static async Task HandleCliCommand(string[] args, IServiceProvider services)
	{
		using var scope = services.CreateScope();
		var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();

		if (args[0] == "add-user" && args.Length >= 3)
		{
			var username = args[1];
			var publicKey = args[2];
			var keyName = args.Length > 3 ? args[3] : null;

			var (success, message) = await userManagementService.CreateUserAsync(username, publicKey, keyName);
			Console.WriteLine(message);
			Environment.Exit(success ? 0 : 1);
		}
		else if (args[0] == "add-key" && args.Length >= 4)
		{
			if (!int.TryParse(args[1], out var userId))
			{
				Console.WriteLine("Error: User ID must be a valid integer");
				Environment.Exit(1);
			}

			var publicKey = args[2];
			var keyName = args[3];

			var (success, error, keyResponse) = await userManagementService.AddSshKeyAsync(userId, publicKey, keyName);
			if (success)
			{
				var fingerprint = keyResponse?.Fingerprint ?? "unknown";
				Console.WriteLine($"SSH key '{keyName}' added with fingerprint: {fingerprint}");
				Environment.Exit(0);
			}

			Console.WriteLine((error ?? SshKeyErrors.InvalidKeyFormat).ToMessage());
			Environment.Exit(1);
		}
		else if (args[0] == "list-users")
		{
			var (success, message) = await userManagementService.ListUsersAsync();
			Console.WriteLine(message);
			Environment.Exit(success ? 0 : 1);
		}
		else
		{
			PrintCliHelp();
			Environment.Exit(1);
		}
	}

	private static void PrintCliHelp()
	{
		Console.WriteLine("Senf CLI Commands:");
		Console.WriteLine();
		Console.WriteLine("  --debug | -d");
		Console.WriteLine("    Enable debug logging (off by default)");
		Console.WriteLine();
		Console.WriteLine("  add-user <username> <public-key> [key-name]");
		Console.WriteLine("    Create a new user with an SSH public key");
		Console.WriteLine("    Example: dotnet run -- add-user john \"ssh-rsa AAAA... user@host\" \"my-key\"");
		Console.WriteLine();
		Console.WriteLine("  add-key <user-id> <public-key> <key-name>");
		Console.WriteLine("    Add an additional SSH key to an existing user");
		Console.WriteLine("    Example: dotnet run -- add-key 1 \"ssh-rsa AAAA... user@host\" \"laptop-key\"");
		Console.WriteLine();
		Console.WriteLine("  list-users");
		Console.WriteLine("    List all users and their SSH keys");
		Console.WriteLine("    Example: dotnet run -- list-users");
		Console.WriteLine();
		Console.WriteLine("To run the API server, use: dotnet run -- run");
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
