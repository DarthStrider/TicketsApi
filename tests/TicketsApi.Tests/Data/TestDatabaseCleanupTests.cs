using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TicketsApi.Data;
using TicketsApi.Tests.Tickets;

namespace TicketsApi.Tests.Data;

// T-005 criterion 16: each test host's temp database (and its WAL/SHM files) is deleted when that host is disposed.
// A host created inside a test deletes its file right away; the class fixture deletes its own when it is disposed;
// a file chosen by the test (FixedDatabaseFactory) is never deleted by a host.
public class TestDatabaseCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factory_deletes_its_databases_when_disposed(bool disposeAsync)
    {
        var factory = new TicketsApiFactory();
        var derived = factory.WithWebHostBuilder(_ => { });

        var files = new List<string>();
        foreach (var app in new WebApplicationFactory<Program>[] { factory, derived })
        {
            await app.CreateClient().CreateTicketAsync("written");
            var path = DatabasePath(app);
            Assert.StartsWith(Path.GetTempPath(), path);
            Assert.True(File.Exists(path), $"Expected the host's database at {path}.");
            files.AddRange([path, path + "-wal", path + "-shm"]);
        }

        Assert.NotEqual(files[0], files[3]); // the derived host has its own file

        derived.Dispose();
        if (disposeAsync)
        {
            await factory.DisposeAsync(); // what xUnit does with a class fixture
        }
        else
        {
            factory.Dispose();
        }

        Assert.All(files, file => Assert.False(File.Exists(file), $"{file} was not deleted."));
    }

    // (a) A host created inside a test deletes its own database as soon as it is disposed, while the class fixture
    // it was derived from is still alive. The fixture's own database is left alone until the fixture is disposed.
    [Theory]
    [InlineData("WithWebHostBuilder", false)]
    [InlineData("WithWebHostBuilder", true)]
    [InlineData("WithClock", false)]
    [InlineData("Nested", false)]
    [InlineData("Kestrel", false)]
    public async Task Derived_host_deletes_its_database_when_it_is_disposed(string kind, bool disposeAsync)
    {
        TicketsApiFactory fixture = kind == "Kestrel" ? new KestrelFactory() : new TicketsApiFactory();
        try
        {
            await fixture.CreateClient().CreateTicketAsync("in the fixture");
            var fixturePath = DatabasePath(fixture);
            Assert.True(File.Exists(fixturePath), $"Expected the fixture's database at {fixturePath}.");

            WebApplicationFactory<Program>? parent = null;
            var derived = kind switch
            {
                "WithClock" => fixture.WithClock(new TestClock(DateTimeOffset.UnixEpoch)),
                "Nested" => (parent = fixture.WithWebHostBuilder(_ => { })).WithWebHostBuilder(_ => { }),
                _ => fixture.WithWebHostBuilder(_ => { }),
            };

            string path;
            try
            {
                HttpClient client;
                if (kind == "Kestrel")
                {
                    // A factory derived with WithWebHostBuilder does not inherit UseKestrel, and its CreateClient()
                    // points at the root's port (Mvc.Testing 10.0.15), so both are set here and then checked.
                    // UseKestrel() without a port: the derived host still gets KestrelFactory's dynamic-port URL,
                    // because ConfigureWebHost runs again, and UseKestrel(port) races the app's start (KestrelFactory).
                    derived.UseKestrel();
                    client = CreateKestrelClient(derived);
                    AssertRunsOnKestrel(derived, client);
                }
                else
                {
                    client = derived.CreateClient();
                }

                await client.CreateTicketAsync("in the derived host");
                path = DatabasePath(derived);
                Assert.NotEqual(fixturePath, path);
                Assert.StartsWith(Path.GetTempPath(), path);
                Assert.True(File.Exists(path), $"Expected the derived host's database at {path}."); // not vacuous
                await AssertStoredInAsync(derived, "in the derived host"); // the request reached the derived host
            }
            finally
            {
                if (disposeAsync)
                {
                    await derived.DisposeAsync();
                }
                else
                {
                    derived.Dispose();
                }
            }

            // The fixture is still alive here: deletion must have happened when the derived host was disposed.
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            {
                Assert.False(File.Exists(file), $"{file} was not deleted when its host was disposed.");
            }

            Assert.True(File.Exists(fixturePath), "Disposing a derived host must not delete the fixture's database.");

            // The fixture keeps working on its own database.
            await fixture.CreateClient().CreateTicketAsync("still works");
            parent?.Dispose();
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    // A class fixture on real Kestrel deletes its own database, and a derived Kestrel host's, when it is disposed.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Kestrel_factory_deletes_its_databases_when_disposed(bool disposeAsync)
    {
        var factory = new KestrelFactory();
        try
        {
            var derived = factory.WithWebHostBuilder(_ => { });
            derived.UseKestrel(); // not inherited, no port: see Derived_host_deletes_its_database_... and KestrelFactory

            var files = new List<string>();
            foreach (var (app, title) in new[] { (factory, "in the root"), (derived, "in the derived host") })
            {
                var client = app == factory ? factory.CreateClient() : CreateKestrelClient(app);
                AssertRunsOnKestrel(app, client);
                await client.CreateTicketAsync(title);
                await AssertStoredInAsync(app, title);
                var path = DatabasePath(app);
                Assert.StartsWith(Path.GetTempPath(), path);
                Assert.True(File.Exists(path), $"Expected the host's database at {path}.");
                files.AddRange([path, path + "-wal", path + "-shm"]);
            }

            Assert.NotEqual(files[0], files[3]); // the derived host has its own file

            if (disposeAsync)
            {
                await factory.DisposeAsync(); // what xUnit does with a class fixture; it disposes the derived host too
            }
            else
            {
                factory.Dispose();
            }

            Assert.All(files, file => Assert.False(File.Exists(file), $"{file} was not deleted."));
        }
        finally
        {
            await factory.DisposeAsync(); // a no-op after the dispose above; cleans up if something threw before it
        }
    }

    // WebApplicationFactory disposes its host with the synchronous Dispose, even from DisposeAsync, so the host
    // wrapper's DisposeAsync is never reached through a factory. This calls it directly on the host the factory built.
    [Fact]
    public async Task Host_deletes_its_database_when_it_is_disposed_asynchronously()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = new HostCapturingFactory();
        try
        {
            await factory.CreateClient().CreateTicketAsync("written");
            var path = DatabasePath(factory);
            Assert.True(File.Exists(path), $"Expected the host's database at {path}."); // not vacuous

            var host = Assert.IsType<IAsyncDisposable>(factory.Host, exactMatch: false);
            await factory.Host.StopAsync(ct);
            await host.DisposeAsync();

            // The factory is still alive, so its fallback has not run: the host deleted its own files.
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            {
                Assert.False(File.Exists(file), $"{file} was not deleted when its host was disposed asynchronously.");
            }
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // (b) A file chosen by the test is never deleted by a host, including hosts derived from a FixedDatabaseFactory.
    [Fact]
    public async Task Fixed_database_is_not_deleted_by_a_derived_host_or_its_root()
    {
        var path = TicketsApiFactory.NewDatabasePath();
        try
        {
            var root = new FixedDatabaseFactory(path);
            try
            {
                var derived = root.WithWebHostBuilder(_ => { });
                try
                {
                    await derived.CreateClient().CreateTicketAsync("in the fixed file");
                    Assert.Equal(path, DatabasePath(derived));
                }
                finally
                {
                    derived.Dispose();
                }

                Assert.True(File.Exists(path), "A derived host deleted a database file chosen by the test.");
            }
            finally
            {
                await root.DisposeAsync();
            }

            Assert.True(File.Exists(path), "The root host deleted a database file chosen by the test.");
        }
        finally
        {
            TestDatabase.Delete(path);
        }

        Assert.False(File.Exists(path)); // the test's own cleanup works
    }

    private static string DatabasePath(WebApplicationFactory<Program> app) =>
        new SqliteConnectionStringBuilder(app.Services.GetRequiredService<TicketsConnectionString>().Value).DataSource;

    // A client for a Kestrel host on that host's own address. Starts the server if needed.
    private static HttpClient CreateKestrelClient(WebApplicationFactory<Program> app)
    {
        app.StartServer();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        return app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(Assert.Single(address ?? [])) });
    }

    // The ticket was written to this host's own database file, so the client's requests reached this host.
    private static async Task AssertStoredInAsync(WebApplicationFactory<Program> app, string title)
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new SqliteConnectionStringBuilder(app.Services.GetRequiredService<TicketsConnectionString>().Value)
        {
            Pooling = false,
        };
        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Tickets WHERE Title = $title";
        command.Parameters.AddWithValue("$title", title);
        Assert.Equal(1L, await command.ExecuteScalarAsync(ct));
    }

    // The host is served by Kestrel (not the in-memory TestServer), and the client talks to it on its real OS port.
    private static void AssertRunsOnKestrel(WebApplicationFactory<Program> app, HttpClient client)
    {
        var server = app.Services.GetRequiredService<IServer>();
        Assert.IsNotType<TestServer>(server);
        Assert.StartsWith("Kestrel", server.GetType().Name);

        var port = client.BaseAddress!.Port;
        Assert.InRange(port, 1, 65535);
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        Assert.Contains(addresses, address => new Uri(address).Port == port);
    }

    // Keeps the host the factory built, which is the database-owning wrapper, so a test can dispose it directly.
    private sealed class HostCapturingFactory : TicketsApiFactory
    {
        public IHost? Host { get; private set; }

        protected override IHost CreateHost(IHostBuilder builder) => Host = base.CreateHost(builder);
    }
}
