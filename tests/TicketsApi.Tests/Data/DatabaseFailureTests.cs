using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
        CancellationToken ct = TestContext.Current.CancellationToken;
        string path = TicketsApiFactory.NewDatabasePath();
        try
        {
            using FixedDatabaseFactory root = new(path);
            using WebApplicationFactory<Program> app = root.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
            HttpClient client = app.CreateClient();

            // The host started on a current schema; the table then disappears under it, so every read and write throws.
            await using (SqliteConnection connection = new($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync(ct);
                await using SqliteCommand drop = connection.CreateCommand();
                drop.CommandText = "DROP TABLE Tickets";
                await drop.ExecuteNonQueryAsync(ct);
            }

            HttpResponseMessage[] responses = new[]
            {
                await client.GetAsync("/tickets", ct),                                      // read (list)
                await client.GetAsync($"/tickets/{Guid.NewGuid()}", ct),                    // read (one)
                await client.PostAsJsonAsync("/tickets", new { title = "lost" }, ct),        // write
            };

            foreach (HttpResponseMessage response in responses)
            {
                JsonElement body = await AssertProblemDetailsAsync(response, HttpStatusCode.InternalServerError);
                Assert.Equal(Texts.Get("Title500", "en"), body.GetProperty("title").GetString());

                string text = await response.Content.ReadAsStringAsync(ct);
                foreach (string leak in new[] { "SQLite", "no such table", "SELECT", "INSERT", "tickets-test-", "Exception", "   at " })
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
