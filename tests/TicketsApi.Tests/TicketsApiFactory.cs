using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TicketsApi.Data;

namespace TicketsApi.Tests;

// The test host for every integration test (T-005 criterion 16). Each host gets its own SQLite database: a temp file
// with a unique name, deleted when that host is disposed. Hosts made with WithWebHostBuilder run ConfigureWebHost
// again, so they get their own empty database too, just like the fresh in-memory store they had before T-005,
// and it is deleted as soon as the derived host is disposed, not when the class fixture is.
public class TicketsApiFactory : WebApplicationFactory<Program>
{
    // The configuration key that tells a host which temp file it owns. A test can override ConnectionStrings:Tickets,
    // but it does not touch this key, so the generated file is still cleaned up.
    private const string OwnedDatabaseKey = "TicketsApiTests:OwnedDatabase";

    private readonly ConcurrentBag<string> _databases = [];
    private readonly string? _fixedDatabasePath;

    public TicketsApiFactory()
    {
    }

    // Used by FixedDatabaseFactory. Protected, because xUnit needs a fixture to have a single public constructor.
    protected TicketsApiFactory(string databasePath, bool applyMigrations)
    {
        _fixedDatabasePath = databasePath;
        ApplyMigrations = applyMigrations;
    }

    protected bool ApplyMigrations { get; } = true;

    public static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"tickets-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var path = _fixedDatabasePath ?? NewDatabasePath();
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:Tickets"] = $"Data Source={path}" };
        if (_fixedDatabasePath is null)
        {
            // A fallback for a host that never finished starting: deleted when this factory is disposed.
            _databases.Add(path);
            settings[OwnedDatabaseKey] = path;
        }

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));

        // Like a deploy step, the schema is applied before the host starts, in every environment the tests use,
        // including Production, where the API itself refuses to start with pending migrations (criteria 15, 20).
        if (ApplyMigrations)
        {
            using var db = new TicketsDbContext(
                new DbContextOptionsBuilder<TicketsDbContext>().UseSqlite(settings["ConnectionStrings:Tickets"]).Options);
            db.Database.Migrate();
        }
    }

    // Every host is built here, including hosts derived with WithWebHostBuilder: in Microsoft.AspNetCore.Mvc.Testing
    // 10.0.15 WithWebHostBuilderCore is internal (it can't be overridden), but the derived factory it creates builds
    // its host through this root factory's CreateHost. So each host is wrapped to delete its own file when the
    // factory that owns it disposes it, synchronously, before that factory's Dispose returns.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        var owned = host.Services.GetRequiredService<IConfiguration>()[OwnedDatabaseKey];
        return owned is null ? host : new DatabaseOwningHost(host, owned);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var path in _databases)
            {
                TestDatabase.Delete(path);
            }
        }
    }

    private sealed class DatabaseOwningHost(IHost inner, string path) : IHost, IAsyncDisposable
    {
        public IServiceProvider Services => inner.Services;

        public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken = default) => inner.StopAsync(cancellationToken);

        public void Dispose()
        {
            inner.Dispose();
            TestDatabase.Delete(path);
        }

        // WebApplicationFactory (10.0.15) disposes its host through the synchronous IHost.Dispose, even from the
        // factory's DisposeAsync (measured: 86 sync calls, 0 async calls over the full suite). This is kept for
        // completeness and is tested directly by Host_deletes_its_database_when_it_is_disposed_asynchronously.
        public async ValueTask DisposeAsync()
        {
            if (inner is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                inner.Dispose();
            }

            TestDatabase.Delete(path);
        }
    }
}

// A host on a database file the test chooses, for tests that need to know or reuse it, such as the restart test
// (T-005 criterion 2). The factory does not delete a fixed file, and neither do hosts derived from it; the test that
// chose it cleans it up.
// applyMigrations: false leaves the database empty, to test what the API itself does at startup (criteria 15, 20).
public sealed class FixedDatabaseFactory(string databasePath, bool applyMigrations = true)
    : TicketsApiFactory(databasePath, applyMigrations);

internal static class TestDatabase
{
    // Deletes a test database and its WAL/SHM side files. SQLite pools connections, which keeps the file open
    // on Windows, so the pools are cleared first.
    public static void Delete(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Best effort: a file still locked by a host being torn down is left in the temp folder.
            }
        }
    }
}
