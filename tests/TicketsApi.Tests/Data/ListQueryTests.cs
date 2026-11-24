using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TicketsApi.Data;
using TicketsApi.Features.Tickets;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Data;

// T-005 criteria 5, 6 and 7: the list is filtered and sorted by the database, in the order Guid.CompareTo gives.
public class ListQueryTests(TicketsApiFactory factory) : IClassFixture<TicketsApiFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // ---- Criterion 5: the database does the sorting (and, for ?status=, the filtering) ----

    [Theory]
    [InlineData("/tickets", false)]
    [InlineData("/tickets?status=open", true)]
    public async Task List_is_sorted_and_filtered_in_sql(string url, bool filtered)
    {
        var ct = TestContext.Current.CancellationToken;
        var recorder = new CommandRecorder();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<TicketsDbContext>(options => options.AddInterceptors(recorder))));
        var client = app.CreateClient();
        await client.CreateTicketAsync("one");
        await client.CreateTicketInStatusAsync("InProgress");
        recorder.Clear();

        var response = await client.GetAsync(url, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sql = Assert.Single(recorder.Commands, c => c.Contains("FROM \"Tickets\"", StringComparison.Ordinal));
        Assert.Matches(new Regex(@"ORDER BY ""\w+""\.""CreatedAt"" DESC, ""\w+""\.""Id"" DESC\s*$"), sql);
        if (filtered)
        {
            Assert.Matches(new Regex(@"WHERE ""\w+""\.""Status"" = "), sql);
        }
        else
        {
            Assert.DoesNotContain("WHERE", sql, StringComparison.Ordinal);
        }
    }

    // ---- Criterion 6: ties are ordered by id descending, exactly as Guid.CompareTo orders them ----

    // Random (v4-like) ids, not the API's v7 ids, so the comparison covers every part of the Guid. Only three
    // createdAt values, so most tickets tie. Seeded, so a failure is repeatable.
    [Fact]
    public async Task Ties_follow_guid_compare_to_for_random_ids()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = TicketsApiFactory.NewDatabasePath();
        try
        {
            using var app = new FixedDatabaseFactory(path);
            var client = app.CreateClient();

            var random = new Random(20261123);
            var seeded = new List<Ticket>();
            for (var i = 0; i < 60; i++)
            {
                var bytes = new byte[16];
                random.NextBytes(bytes);
                seeded.Add(new Ticket
                {
                    Id = new Guid(bytes),
                    Title = $"tie {i}",
                    Description = null,
                    Status = TicketStatus.Open,
                    CreatedAt = T0.AddTicks(random.Next(3)),
                });
            }

            // Written through EF Core itself, so the ids are stored exactly as the API stores them.
            await using (var db = new TicketsDbContext(
                new DbContextOptionsBuilder<TicketsDbContext>().UseSqlite($"Data Source={path}").Options))
            {
                db.Tickets.AddRange(seeded);
                await db.SaveChangesAsync(ct);
            }

            var body = await ReadJsonAsync(await client.GetAsync("/tickets", ct), ct);
            var ids = body.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToList();

            var expected = seeded
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id, Comparer<Guid>.Create((a, b) => a.CompareTo(b)))
                .Select(t => t.Id)
                .ToList();
            Assert.Equal(3, seeded.Select(t => t.CreatedAt).Distinct().Count());
            Assert.Equal(expected, ids);
        }
        finally
        {
            TestDatabase.Delete(path);
        }
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
