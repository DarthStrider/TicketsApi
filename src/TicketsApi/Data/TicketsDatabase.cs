using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace TicketsApi.Data;

// T-005 criteria 11, 12, 15 and 20: where the database is and what happens to its schema at startup.
internal static class TicketsDatabase
{
    public const string ConnectionStringKey = "ConnectionStrings:Tickets";

    public static IServiceCollection AddTicketsDatabase(this IServiceCollection services)
    {
        // Resolved once per host, on first use. Not while Program.cs builds the app, so configuration added later
        // (environment variables, test hosts) is the one used, and not per request.
        services.AddSingleton(provider => new TicketsConnectionString(GetConnectionString(
            provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<IHostEnvironment>())));

        services.AddDbContext<TicketsDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<TicketsConnectionString>().Value));

        return services;
    }

    // Fails fast on a missing connection string, creates the database folder, then makes sure the schema is
    // current: Development applies pending migrations; every other environment (Production, Staging, ...)
    // refuses to start with pending migrations, because they are applied before deploy (criterion 20).
    public static async Task InitializeTicketsDatabaseAsync(this WebApplication app)
    {
        string connectionString = app.Services.GetRequiredService<TicketsConnectionString>().Value;
        CreateDatabaseFolder(connectionString);

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        DatabaseFacade database = scope.ServiceProvider.GetRequiredService<TicketsDbContext>().Database;

        if (app.Environment.IsDevelopment())
        {
            await database.MigrateAsync();
            return;
        }

        List<string> pending = (await database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            throw new InvalidOperationException(
                $"The tickets database has {pending.Count} pending migrations ({string.Join(", ", pending)}). " +
                "Apply them before starting the API, for example with `dotnet ef database update`.");
        }
    }

    // A relative Data Source is resolved against the content root, not the current directory, so
    // `App_Data/tickets.dev.db` works however the API is started. No side effects: see CreateDatabaseFolder.
    public static string GetConnectionString(IConfiguration configuration, IHostEnvironment environment)
    {
        string? value = configuration.GetConnectionString("Tickets");
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The connection string '{ConnectionStringKey}' is missing. Set it in configuration, " +
                "for example with the environment variable ConnectionStrings__Tickets.");
        }

        SqliteConnectionStringBuilder builder = new(value);
        if (IsFile(builder) && !Path.IsPathFullyQualified(builder.DataSource))
        {
            builder.DataSource = Path.Combine(environment.ContentRootPath, builder.DataSource);
        }

        return builder.ToString();
    }

    // SQLite creates the file but not its folder. Internal so the tests can check it (criterion 11).
    internal static void CreateDatabaseFolder(string connectionString)
    {
        SqliteConnectionStringBuilder builder = new(connectionString);
        string? folder = IsFile(builder) ? Path.GetDirectoryName(builder.DataSource) : null;
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }
    }

    // A plain file path. In-memory databases and "file:" URIs are used as given.
    private static bool IsFile(SqliteConnectionStringBuilder builder) =>
        builder.Mode != SqliteOpenMode.Memory
        && !string.IsNullOrEmpty(builder.DataSource)
        && builder.DataSource != ":memory:"
        && !builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
}

// The resolved connection string for this host (singleton).
internal sealed record TicketsConnectionString(string Value);
