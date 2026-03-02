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
		var builder = WebApplication.CreateBuilder(args);

		// Configure logging to see SSH authentication details
		builder.Logging.ClearProviders();
		builder.Logging.AddConsole();
		builder.Logging.SetMinimumLevel(LogLevel.Debug);
		builder.Logging.AddFilter("Senf.Authentication.SshAuthenticationHandler", LogLevel.Debug);
		builder.Logging.AddFilter("Senf.Services.SshAuthService", LogLevel.Debug);

		// Support both container and local deployments
		string dbPath;
		var dbPathEnv = Environment.GetEnvironmentVariable("DATABASE_PATH");

		if (!string.IsNullOrEmpty(dbPathEnv))
		{
			// Use environment variable (for containers)
			dbPath = dbPathEnv;
			Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
		}
		else
		{
			// Use ApplicationData folder (for local/Windows development)
			var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			var dbDirectory = Path.Combine(appDataPath, "Senf");
			Directory.CreateDirectory(dbDirectory);
			dbPath = Path.Combine(dbDirectory, "senf.db");
		}

		builder.Services.AddDbContext<AppDbContext>(options =>
			options.UseSqlite($"Data Source={dbPath}"));

		builder.Services.AddDataProtection();

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

		// Configure port from environment variable or use default
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

		if (args.Length > 0 && args[0] != "run")
		{
			await HandleCliCommand(args, app.Services);
			return;
		}

		if (app.Environment.IsDevelopment())
			app.MapOpenApi();

		app.UseHttpsRedirection();
		app.UseAuthentication();
		app.UseAuthorization();

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

			var (success, message, _) = await userManagementService.AddSshKeyAsync(userId, publicKey, keyName);
			Console.WriteLine(message);
			Environment.Exit(success ? 0 : 1);
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
}