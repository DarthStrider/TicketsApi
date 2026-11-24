using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketsApi.Data;
using TicketsApi.Features.Tickets;

namespace TicketsApi.Tests.Data;

// T-005 criteria 1, 13, 14 and 17.
public class ModelTests(TicketsApiFactory factory) : IClassFixture<TicketsApiFactory>
{
    // Criteria 1 and 17: the API stores tickets with EF Core in a real SQLite file (this host's temp file).
    [Fact]
    public void Store_is_ef_core_on_a_sqlite_file()
    {
        using var scope = factory.Services.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        var db = scope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;

        Assert.IsType<EfTicketStore>(store);
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        Assert.StartsWith(Path.GetTempPath(), dataSource);
        Assert.EndsWith(".db", dataSource);
    }

    // Criteria 13 and 14: the committed migrations match the model, so no change was made without a migration.
    [Fact]
    public void Model_has_no_pending_changes()
    {
        using var db = new DesignTimeTicketsDbContextFactory().CreateDbContext([]);

        Assert.NotEmpty(db.Database.GetMigrations());
        Assert.False(db.Database.HasPendingModelChanges(), "The model changed without a migration: run `dotnet ef migrations add`.");
    }
}
