using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using TicketsApi.Tests.Localization;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Data;

// T-005 criterion 18 (the optional variant): a real broken database, not a ThrowingTicketStore, gives the existing
// localized 500 ProblemDetails, with no exception details, SQL or file path, in Development and Production.
public class DatabaseFailureTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Broken_database_returns_500_without_details(string environment)
    {
        var ct = TestContext.Current.CancellationToken;
        var path = TicketsApiFactory.NewDatabasePath();
        try
        {
            using var root = new FixedDatabaseFactory(path);
            using var app = root.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
            var client = app.CreateClient();

            // The host started on a current schema; the table then disappears under it, so every read and write throws.
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync(ct);
                await using var drop = connection.CreateCommand();
                drop.CommandText = "DROP TABLE Tickets";
                await drop.ExecuteNonQueryAsync(ct);
            }

            var responses = new[]
            {
                await client.GetAsync("/tickets", ct),                                      // read (list)
                await client.GetAsync($"/tickets/{Guid.NewGuid()}", ct),                    // read (one)
                await client.PostAsJsonAsync("/tickets", new { title = "lost" }, ct),        // write
            };

            foreach (var response in responses)
            {
                var body = await AssertProblemDetailsAsync(response, HttpStatusCode.InternalServerError);
                Assert.Equal(Texts.Get("Title500", "en"), body.GetProperty("title").GetString());

                var text = await response.Content.ReadAsStringAsync(ct);
                foreach (var leak in new[] { "SQLite", "no such table", "SELECT", "INSERT", "tickets-test-", "Exception", "   at " })
                {
                    Assert.DoesNotContain(leak, text, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        finally
        {
            TestDatabase.Delete(path);
        }
    }
}
