using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TicketsApi.Tests.Tickets;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Data;

// T-005: tickets are stored in SQLite through EF Core.
public class PersistenceTests(TicketsApiFactory factory) : IClassFixture<TicketsApiFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // ---- Criterion 2: data survives a restart ----

    [Fact]
    public async Task Data_survives_a_restart()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string path = TicketsApiFactory.NewDatabasePath();
        try
        {
            string created;
            Guid id;
            using (FixedDatabaseFactory first = new(path))
            {
                HttpClient client = first.CreateClient();
                id = (await client.CreateTicketAsync("Survives", "a restart")).GetProperty("id").GetGuid();
                HttpResponseMessage patch = await client.PatchStatusAsync(id, new { status = "InProgress" });
                Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
                created = await patch.Content.ReadAsStringAsync(ct);
            }

            using FixedDatabaseFactory second = new(path);
            HttpResponseMessage response = await second.CreateClient().GetAsync($"/tickets/{id}", ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(created, await response.Content.ReadAsStringAsync(ct));
        }
        finally
        {
            TestDatabase.Delete(path);
        }
    }

    // ---- Criterion 16: each test host has its own database ----

    [Fact]
    public async Task Parallel_hosts_do_not_share_tickets()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> a = factory.WithWebHostBuilder(_ => { });
        using WebApplicationFactory<Program> b = factory.WithWebHostBuilder(_ => { });

        await a.CreateClient().CreateTicketAsync("only in A");
        JsonElement inA = await ReadJsonAsync(await a.CreateClient().GetAsync("/tickets", ct), ct);
        JsonElement inB = await ReadJsonAsync(await b.CreateClient().GetAsync("/tickets", ct), ct);

        Assert.Equal(1, inA.GetProperty("items").GetArrayLength()); // so B's 0 can't pass because reads are broken
        Assert.Equal(0, inB.GetProperty("items").GetArrayLength());
    }

    // ---- Criteria 3 and 4: exact round-trip of text and createdAt ----

    // Exactly 200 and 4000 UTF-16 code units, mixing a 1-unit "é" and a 2-unit "😀".
    private static readonly string MaxTitle = string.Concat(Enumerable.Repeat("é😀", 66)) + "éé";
    private static readonly string MaxDescription = string.Concat(Enumerable.Repeat("é😀", 1333)) + "é";

    [Theory]
    [InlineData("max")]
    [InlineData("null")]
    [InlineData("empty")]
    public async Task Text_and_created_at_round_trip_exactly(string descriptionCase)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Assert.Equal(200, MaxTitle.Length);
        Assert.Equal(4000, MaxDescription.Length);

        // A time with every one of the seven sub-second digits set (100 ns ticks).
        using WebApplicationFactory<Program> app = factory.WithClock(new TestClock(T0.AddTicks(1_234_567)));
        HttpClient client = app.CreateClient();
        string? description = descriptionCase switch
        {
            "max" => MaxDescription,
            "empty" => "",
            _ => null,
        };

        JsonElement created = await client.CreateTicketAsync(MaxTitle, description);
        Guid id = created.GetProperty("id").GetGuid();
        JsonElement get = await ReadJsonAsync(await client.GetAsync($"/tickets/{id}", ct), ct);
        JsonElement list = await ReadJsonAsync(await client.GetAsync("/tickets", ct), ct);

        Assert.Equal("2026-01-01T12:00:00.1234567+00:00", created.GetProperty("createdAt").GetString());
        Assert.Equal(created.GetRawText(), get.GetRawText());
        Assert.Equal(created.GetRawText(), Assert.Single(list.GetProperty("items").EnumerateArray()).GetRawText());
        Assert.Equal(MaxTitle, get.GetProperty("title").GetString());
        Assert.Equal(description, get.GetProperty("description").GetString());
    }

    // ---- Criteria 5 to 8: how a ticket is stored ----

    [Fact]
    public async Task Storage_format_is_as_specified()
    {
        string path = TicketsApiFactory.NewDatabasePath();
        try
        {
            Guid id;
            DateTimeOffset createdAt;
            using (FixedDatabaseFactory app = new(path))
            {
                HttpClient client = app.CreateClient();
                JsonElement created = await client.CreateTicketAsync("Stored");
                id = created.GetProperty("id").GetGuid();
                createdAt = created.GetProperty("createdAt").GetDateTimeOffset();
                await client.PatchStatusAsync(id, new { status = "InProgress" });
                await client.PatchStatusAsync(id, new { status = "Resolved" });
            }

            await using SqliteConnection connection = new($"Data Source={path};Mode=ReadOnly;Pooling=False");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT Id, typeof(Id), Status, typeof(Status), CreatedAt, typeof(CreatedAt), Version FROM Tickets";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(id.ToString().ToUpperInvariant(), reader.GetString(0)); // criterion 6: uppercase TEXT id
            Assert.Equal("text", reader.GetString(1));
            Assert.Equal("Resolved", reader.GetString(2));                       // criterion 7: the status name
            Assert.Equal("text", reader.GetString(3));
            Assert.Equal(createdAt.UtcTicks, reader.GetInt64(4));                // criteria 4, 5: UTC ticks
            Assert.Equal("integer", reader.GetString(5));
            Assert.Equal(2, reader.GetInt64(6));                                 // criterion 8: one per status write
            Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            TestDatabase.Delete(path);
        }
    }

    // ---- Criterion 10: the version is never exposed ----

    [Fact]
    public async Task Version_is_not_exposed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpClient client = factory.CreateClient();
        HttpResponseMessage post = await client.PostAsJsonAsync("/tickets", new { title = "No version" }, ct);
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        JsonElement postBody = await ReadJsonAsync(post, ct);
        Guid id = postBody.GetProperty("id").GetGuid();

        HttpResponseMessage patch = await client.PatchStatusAsync(id, new { status = "InProgress" });
        HttpResponseMessage get = await client.GetAsync($"/tickets/{id}", ct);
        HttpResponseMessage list = await client.GetAsync("/tickets", ct);

        string[] fields = ["id", "title", "description", "status", "createdAt"];
        foreach (HttpResponseMessage response in new[] { post, patch, get, list })
        {
            List<string> headers = response.Headers.Concat(response.Content.Headers).Select(h => h.Key).ToList();
            Assert.DoesNotContain(headers, h => h.Contains("ETag", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(headers, h => h.Contains("Version", StringComparison.OrdinalIgnoreCase));

            JsonElement body = response == post ? postBody : await ReadJsonAsync(response, ct);
            List<JsonElement> tickets = response == list ? body.GetProperty("items").EnumerateArray().ToList() : [body];
            Assert.NotEmpty(tickets);
            Assert.All(tickets, ticket => Assert.Equal(fields, ticket.EnumerateObject().Select(p => p.Name)));
        }
    }

    // ---- Criteria 11 and 12: the connection string ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_connection_string_fails_startup(string? value)
    {
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Tickets"] = value })));

        InvalidOperationException error = StartupError(() => app.CreateClient());

        Assert.Contains("connection string 'ConnectionStrings:Tickets' is missing", error.Message);
    }

    // ---- Criteria 15 and 20: migrations at startup ----

    [Fact]
    public async Task Development_host_migrates_an_empty_database()
    {
        string path = TicketsApiFactory.NewDatabasePath();
        try
        {
            using FixedDatabaseFactory app = new(path, applyMigrations: false);

            HttpResponseMessage response = await app.CreateClient().PostAsync(
                "/tickets",
                new StringContent("""{ "title": "migrated" }""", System.Text.Encoding.UTF8, "application/json"),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        finally
        {
            TestDatabase.Delete(path);
        }
    }

    // Criterion 20: every environment other than Development refuses to start with pending migrations.
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Non_development_with_pending_migrations_fails_startup(string environment)
    {
        string path = TicketsApiFactory.NewDatabasePath();
        try
        {
            using FixedDatabaseFactory root = new(path, applyMigrations: false);
            using WebApplicationFactory<Program> app = root.WithWebHostBuilder(builder => builder.UseEnvironment(environment));

            InvalidOperationException error = StartupError(() => app.CreateClient());

            Assert.Contains("pending migrations", error.Message);
            Assert.Contains("InitialCreate", error.Message);
        }
        finally
        {
            TestDatabase.Delete(path);
        }
    }

    // The API's own startup error, wherever the host wraps it. Checking the exception's message, not the whole
    // stack trace, so an unrelated exception from EF's Migrations namespace can't make the test pass.
    private static InvalidOperationException StartupError(Action start)
    {
        Exception? error = Record.Exception(start);
        Assert.NotNull(error);

        // InnerException also walks into an AggregateException (it returns the first inner exception).
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException found && current.Source == "TicketsApi")
            {
                return found;
            }
        }

        Assert.Fail($"Startup failed, but not with the API's own InvalidOperationException: {error}");
        return null!;
    }
}
