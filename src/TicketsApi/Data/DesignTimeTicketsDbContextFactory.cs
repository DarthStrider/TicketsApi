using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TicketsApi.Data;

// Used only by `dotnet ef` (for example `dotnet ef migrations add`), so the tool doesn't have to start the app.
// It names no database on purpose (T-005 criteria 11 and 12). Adding a migration doesn't need one. For
// `dotnet ef database update`, --connection replaces this whole value; without it, SQLite would open a throwaway
// temporary database and report success while changing nothing, so it's opened read-only and the update fails.
internal sealed class DesignTimeTicketsDbContextFactory : IDesignTimeDbContextFactory<TicketsDbContext>
{
    public TicketsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TicketsDbContext>().UseSqlite("Mode=ReadOnly").Options);
}
