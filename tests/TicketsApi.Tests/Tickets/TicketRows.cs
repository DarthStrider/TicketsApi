using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TicketsApi.Tests.Tickets;

// Reads and writes ticket rows directly in a test host's SQLite database, for what the API can't show:
// the stored creator (T-006 criterion 14), rows from before T-006 (criterion 18), and the status of a ticket
// the caller isn't allowed to read (criteria 20 and 21).
internal static class TicketRows
{
    public static Task<string?> CreatedByAsync(WebApplicationFactory<Program> app, Guid id) =>
        ScalarAsync(app, "SELECT CreatedBy FROM Tickets WHERE Id = $id", id);

    public static Task<string?> StatusAsync(WebApplicationFactory<Program> app, Guid id) =>
        ScalarAsync(app, "SELECT Status FROM Tickets WHERE Id = $id", id);

    // A row as it was stored before T-006: no creator.
    public static async Task InsertWithoutCreatorAsync(WebApplicationFactory<Program> app, Guid id, string title)
    {
        await using SqliteConnection connection = await OpenAsync(app);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO Tickets (Id, Title, Description, Status, CreatedAt, Version, CreatedBy) " +
            "VALUES ($id, $title, NULL, 'Open', 0, 0, NULL)";
        command.Parameters.AddWithValue("$id", Key(id));
        command.Parameters.AddWithValue("$title", title);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<string?> ScalarAsync(WebApplicationFactory<Program> app, string sql, Guid id)
    {
        await using SqliteConnection connection = await OpenAsync(app);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", Key(id));
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) as string;
    }

    // Ids are stored as uppercase TEXT (T-005 criterion 6).
    private static string Key(Guid id) => id.ToString().ToUpperInvariant();

    private static async Task<SqliteConnection> OpenAsync(WebApplicationFactory<Program> app)
    {
        string connectionString = app.Services.GetRequiredService<IConfiguration>().GetConnectionString("Tickets")
            ?? throw new InvalidOperationException("The test host has no ConnectionStrings:Tickets.");
        SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }
}
