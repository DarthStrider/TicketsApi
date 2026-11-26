using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TicketsApi.Data;
using TicketsApi.Features.Tickets;
using TicketsApi.Tests.Auth;
using TicketsApi.Tests.Tickets;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Data;

// T-006 criteria 14, 17, 18 and 19 at the database level: the CreatedBy column, its migration, the list filter's SQL,
// and that the stored creator round-trips and survives a status change.
public class CreatorStorageTests(TicketsApiFactory factory) : IClassFixture<TicketsApiFactory>
{
    // The last migration of T-005: the schema an existing database has before T-006 is deployed.
    private const string T005Migration = "20261123153609_InitialCreate";
    private const string T006Migration = "20261125144214_AddTicketCreatedBy";

    private static readonly Guid OldOpen = Guid.Parse("0193a000-0000-7000-8000-000000000001");
    private static readonly Guid OldClosed = Guid.Parse("0193a000-0000-7000-8000-000000000002");

    // ---- Criteria 18 and 19: an existing T-005 database is migrated without losing a row ----

    [Fact]
    public async Task Migrating_a_t005_database_keeps_every_row_with_no_creator()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string path = TicketsApiFactory.NewDatabasePath();
        try
        {
            await MigrateToAsync(path, T005Migration);
            await ExecuteAsync(path, InsertT005Rows);
            List<string> before = await RowsAsync(path, "Id, Title, Description, Status, CreatedAt, Version");

            // FixedDatabaseFactory applies every pending migration before the host starts, like a deploy step.
            // The host is built (and the migration applied) by the first CreateClient.
            using FixedDatabaseFactory app = new(path);
            HttpClient agent = app.CreateClientAs("carol;agent");
            HttpClient alice = app.CreateClientAs("alice");

            Assert.Equal(before, await RowsAsync(path, "Id, Title, Description, Status, CreatedAt, Version"));
            Assert.Equal(["NULL|null", "NULL|null"], await RowsAsync(path, "quote(CreatedBy), typeof(CreatedBy)"));
            Assert.Equal([T005Migration, T006Migration], await RowsAsync(path, "MigrationId", "__EFMigrationsHistory"));

            // The rows are still readable through the API, by agents only (criterion 18).
            Assert.Equal([OldClosed, OldOpen], await ListIdsAsync(agent, ct));
            Assert.Empty(await ListIdsAsync(alice, ct));
            Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync($"/tickets/{OldOpen}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/tickets/{OldOpen}", ct)).StatusCode);
        }
        finally
        {
            TestDatabase.Delete(path);
        }
    }

    // The column is nullable TEXT with the single-column index the non-agent list uses.
    [Fact]
    public async Task Created_by_column_is_nullable_text_and_indexed()
    {
        string path = TicketsApiFactory.NewDatabasePath();
        try
        {
            await MigrateToAsync(path, null);

            Assert.Contains("CreatedBy|TEXT|0", await RowsAsync(path, "name, type, \"notnull\"", "pragma_table_info('Tickets')"));
            Assert.Contains("IX_Tickets_CreatedBy|0", await RowsAsync(path, "name, \"unique\"", "pragma_index_list('Tickets')"));
            Assert.Equal(["CreatedBy"], await RowsAsync(path, "name", "pragma_index_info('IX_Tickets_CreatedBy')"));
        }
        finally
        {
            TestDatabase.Delete(path);
        }
    }

    // Down() rebuilds the table without CreatedBy: every row and every other value is kept. Up() again gives NULL.
    [Fact]
    public async Task Migration_down_keeps_every_row_and_up_again_gives_no_creator()
    {
        string path = TicketsApiFactory.NewDatabasePath();
        try
        {
            await MigrateToAsync(path, null);
            await ExecuteAsync(path, InsertT005Rows);
            await ExecuteAsync(path, "UPDATE Tickets SET CreatedBy = 'alice'");
            List<string> before = await RowsAsync(path, "Id, Title, Description, Status, CreatedAt, Version");

            await MigrateToAsync(path, T005Migration);

            Assert.Equal(before, await RowsAsync(path, "Id, Title, Description, Status, CreatedAt, Version"));
            Assert.DoesNotContain("CreatedBy", await RowsAsync(path, "name", "pragma_table_info('Tickets')"));
            Assert.DoesNotContain("IX_Tickets_CreatedBy", await RowsAsync(path, "name", "pragma_index_list('Tickets')"));

            await MigrateToAsync(path, null);

            Assert.Equal(before, await RowsAsync(path, "Id, Title, Description, Status, CreatedAt, Version"));
            Assert.Equal(["NULL", "NULL"], await RowsAsync(path, "quote(CreatedBy)"));
        }
        finally
        {
            TestDatabase.Delete(path);
        }
    }

    // ---- Criterion 17: the non-agent list is filtered by the database, never in memory ----

    // A non-null creator must compare with "=" only: no "IS NULL" branch, so rows without a creator never match.
    [Theory]
    [InlineData("/tickets", false)]
    [InlineData("/tickets?status=open", true)]
    public async Task Non_agent_list_filters_on_created_by_in_sql(string url, bool byStatus)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        CommandRecorder recorder = new();
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<TicketsDbContext>(options => options.AddInterceptors(recorder))));
        HttpClient alice = app.CreateClientAs("alice");
        await alice.CreateTicketAsync("mine");
        await app.CreateClientAs("bob").CreateTicketAsync("bob's");
        recorder.Clear();

        HttpResponseMessage response = await alice.GetAsync(url, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(response, ct)).GetProperty("items").GetArrayLength());
        string sql = Assert.Single(recorder.Commands, c => c.Contains("FROM \"Tickets\"", StringComparison.Ordinal));
        Assert.Matches(new Regex(@"WHERE .*""\w+""\.""CreatedBy"" = @\w+", RegexOptions.Singleline), sql);
        Assert.DoesNotContain("IS NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(new Regex(@"ORDER BY ""\w+""\.""CreatedAt"" DESC, ""\w+""\.""Id"" DESC\s*$"), sql);
        Assert.Equal(byStatus, Regex.IsMatch(sql, @"""\w+""\.""Status"" = "));
    }

    // The store's filter: exact, case-sensitive (like TicketAccess.CanRead's ordinal compare), never matches NULL,
    // and null means every ticket.
    [Fact]
    public async Task Store_list_filter_matches_the_creator_exactly()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        using IServiceScope scope = app.Services.CreateScope();
        ITicketStore store = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        Dictionary<string, Guid> ids = new();
        int minute = 0;
        foreach (string? creator in new[] { "alice", "Alice", "alice ", "", null, "bob", "alice" })
        {
            Ticket ticket = NewTicket(creator, minute++);
            await store.AddAsync(ticket, ct);
            ids[$"{minute}:{creator ?? "<null>"}"] = ticket.Id;
        }

        IReadOnlyList<Ticket> alices = await store.ListAsync(null, "alice", ct);
        IReadOnlyList<Ticket> empties = await store.ListAsync(null, "", ct);
        IReadOnlyList<Ticket> everyone = await store.ListAsync(null, null, ct);

        Assert.Equal([ids["7:alice"], ids["1:alice"]], alices.Select(t => t.Id));
        Assert.Equal([ids["4:"]], empties.Select(t => t.Id));
        Assert.Equal(7, everyone.Count);
        Assert.Contains(everyone, t => t.CreatedBy is null);
    }

    // ---- Criterion 14: the creator round-trips exactly, whatever the provider's sub looks like ----

    [Theory]
    [InlineData("auth0|65f0c1d2e3a4b5c6d7e8f901")]
    [InlineData("google-oauth2|103456789012345678901")]
    [InlineData("00u1a2b3c4D5E6F7g8h9")]
    [InlineData("f81d4fae-7dec-11d0-a765-00a0c91e6bf6")]
    [InlineData("a' OR '1'='1; DROP TABLE Tickets;--")]
    [InlineData("Zoë 😀 ünïcödé")]
    [InlineData("max")]
    public async Task Creator_round_trips_exactly(string sub)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        if (sub == "max")
        {
            sub = new string('x', 255); // OpenID Connect: a sub is at most 255 ASCII characters
        }

        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        using IServiceScope scope = app.Services.CreateScope();
        ITicketStore store = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        Ticket ticket = NewTicket(sub, 0);

        await store.AddAsync(ticket, ct);

        Assert.Equal(sub, (await store.GetAsync(ticket.Id, ct))?.CreatedBy);
        Assert.Equal(sub, Assert.Single(await store.ListAsync(null, sub, ct)).CreatedBy);
        Assert.Equal(sub, await TicketRows.CreatedByAsync(app, ticket.Id));
    }

    // ---- A status change never changes or drops the creator ----

    [Theory]
    [InlineData("alice")]
    [InlineData(null)]
    public async Task Status_change_keeps_the_creator(string? creator)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        using IServiceScope scope = app.Services.CreateScope();
        ITicketStore store = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        Ticket ticket = NewTicket(creator, 0);
        await store.AddAsync(ticket, ct);

        Ticket? updated = await store.TryUpdateStatusAsync(ticket.Id, TicketStatus.Open, TicketStatus.InProgress, ct);

        Assert.NotNull(updated);
        Assert.Equal(creator, updated.CreatedBy);
        Assert.Equal(creator, (await store.GetAsync(ticket.Id, ct))?.CreatedBy);
        Assert.Equal(creator, await TicketRows.CreatedByAsync(app, ticket.Id));
    }

    // ---- Helpers ----

    // Two rows as T-005 stored them: one with a description and a version that has moved.
    private const string InsertT005Rows =
        "INSERT INTO Tickets (Id, Title, Description, Status, CreatedAt, Version) VALUES " +
        "('0193A000-0000-7000-8000-000000000001', 'Old open', NULL, 'Open', 638000000000000000, 0), " +
        "('0193A000-0000-7000-8000-000000000002', 'Old closé 😀', 'd', 'Closed', 638000000000000001, 3)";

    private static Ticket NewTicket(string? creator, int minute) => new()
    {
        Id = Guid.CreateVersion7(),
        Title = $"t{minute}",
        Description = null,
        Status = TicketStatus.Open,
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, minute, 0, TimeSpan.Zero),
        CreatedBy = creator,
    };

    // Applies the migrations up (or down) to `target`; null means the latest.
    private static async Task MigrateToAsync(string path, string? target)
    {
        await using TicketsDbContext db = new(
            new DbContextOptionsBuilder<TicketsDbContext>().UseSqlite($"Data Source={path}").Options);
        await db.GetService<IMigrator>().MigrateAsync(target, TestContext.Current.CancellationToken);
    }

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using SqliteConnection connection = new($"Data Source={path};Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    // Each row as "v1|v2|...", in a stable order.
    private static async Task<List<string>> RowsAsync(string path, string columns, string from = "Tickets")
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using SqliteConnection connection = new($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(ct);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {columns} FROM {from} ORDER BY 1";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct);
        List<string> rows = new();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "<null>" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    private static async Task<List<Guid>> ListIdsAsync(HttpClient client, CancellationToken ct)
    {
        HttpResponseMessage response = await client.GetAsync("/tickets", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await ReadJsonAsync(response, ct)).GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetGuid())];
    }

    // Records the SQL of every query the API runs.
    private sealed class CommandRecorder : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public IReadOnlyList<string> Commands => [.. _commands];

        public void Clear() => _commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            _commands.Enqueue(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
