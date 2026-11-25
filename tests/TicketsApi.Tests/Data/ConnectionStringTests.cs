using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TicketsApi.Data;
using TicketsApi.Tests.Tickets;

namespace TicketsApi.Tests.Data;

// T-005 criterion 11: where the connection string comes from and which file it points to.
public class ConnectionStringTests(TicketsApiFactory factory) : IClassFixture<TicketsApiFactory>
{
    private static readonly string ContentRoot = Path.Combine(Path.GetTempPath(), "tickets-content-root");

    // ---- What GetConnectionString returns ----

    [Theory]
    [InlineData("App_Data/tickets.dev.db")]
    [InlineData("tickets.db")]
    [InlineData("a/b/c.db")]
    public void Relative_data_source_is_resolved_against_the_content_root(string relative)
    {
        string resolved = DataSourceOf(Resolve($"Data Source={relative}"));

        Assert.Equal(Path.GetFullPath(Path.Combine(ContentRoot, relative)), Path.GetFullPath(resolved));
        Assert.True(Path.IsPathFullyQualified(resolved));
    }

    [Fact]
    public void Other_keys_are_kept_when_the_data_source_is_resolved()
    {
        SqliteConnectionStringBuilder builder = new(Resolve("Data Source=x.db;Cache=Shared;Pooling=False"));

        Assert.Equal(SqliteCacheMode.Shared, builder.Cache);
        Assert.False(builder.Pooling);
    }

    [Fact]
    public void Absolute_data_source_is_unchanged()
    {
        string absolute = Path.Combine(Path.GetTempPath(), "elsewhere", "tickets.db");

        Assert.Equal(absolute, DataSourceOf(Resolve($"Data Source={absolute}")));
    }

    [Theory]
    [InlineData("Data Source=:memory:", ":memory:")]
    [InlineData("Data Source=shared;Mode=Memory;Cache=Shared", "shared")]
    [InlineData("Data Source=file:tickets.db?mode=ro", "file:tickets.db?mode=ro")]
    public void In_memory_and_uri_data_sources_are_unchanged(string connectionString, string dataSource)
    {
        Assert.Equal(dataSource, DataSourceOf(Resolve(connectionString)));
    }

    // ---- The database folder ----

    [Fact]
    public void Create_database_folder_creates_missing_folders()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tickets-folder-{Guid.NewGuid():N}");
        string folder = Path.Combine(root, "nested", "deeper");
        try
        {
            TicketsDatabase.CreateDatabaseFolder($"Data Source={Path.Combine(folder, "tickets.db")}");

            Assert.True(Directory.Exists(folder));
            Assert.Empty(Directory.EnumerateFileSystemEntries(folder)); // the folder only; SQLite makes the file
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // End to end: a host started with a relative Data Source creates its database under its content root,
    // in a folder that didn't exist, not in the current directory.
    [Fact]
    public async Task Host_with_relative_data_source_creates_the_database_under_its_content_root()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tickets-root-{Guid.NewGuid():N}");
        string relative = Path.Combine("App_Data", $"relative-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(root);
        try
        {
            using (WebApplicationFactory<Program> app = factory.WithWebHostBuilder(builder =>
            {
                builder.UseContentRoot(root);
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["ConnectionStrings:Tickets"] = $"Data Source={relative}" }));
            }))
            {
                HttpResponseMessage response = await app.CreateClient().PostAsync(
                    "/tickets",
                    new StringContent("""{ "title": "relative" }""", System.Text.Encoding.UTF8, "application/json"),
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                Assert.Equal(root, app.Services.GetRequiredService<IHostEnvironment>().ContentRootPath.TrimEnd('\\', '/'));
            }

            Assert.True(File.Exists(Path.Combine(root, relative)), "The database was not created under the content root.");
            Assert.False(File.Exists(Path.GetFullPath(relative)), "The database was created in the current directory.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- What the app's own appsettings files say ----

    [Fact]
    public void Development_settings_point_to_app_data_and_base_settings_have_no_default()
    {
        string contentRoot = factory.Services.GetRequiredService<IHostEnvironment>().ContentRootPath;

        IConfigurationRoot baseSettings = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        IConfigurationRoot development = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();

        Assert.Null(baseSettings.GetConnectionString("Tickets")); // Production: no default (criteria 11, 12)
        Assert.Equal("Data Source=App_Data/tickets.dev.db", development.GetConnectionString("Tickets"));
    }

    // ---- Helpers ----

    private static string Resolve(string connectionString) =>
        TicketsDatabase.GetConnectionString(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Tickets"] = connectionString })
                .Build(),
            new StubEnvironment(ContentRoot));

    private static string DataSourceOf(string connectionString) =>
        new SqliteConnectionStringBuilder(connectionString).DataSource;

    private sealed class StubEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "TicketsApi";

        public string ContentRootPath { get; set; } = contentRoot;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
