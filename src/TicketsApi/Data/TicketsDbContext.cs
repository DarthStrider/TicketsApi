using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketsApi.Features.Tickets;

namespace TicketsApi.Data;

// T-005: tickets in SQLite. The storage choices below are product decisions recorded in the ticket.
internal sealed class TicketsDbContext(DbContextOptions<TicketsDbContext> options) : DbContext(options)
{
    // The concurrency token (criterion 8): incremented on every status write. A shadow property, so it is not on
    // Ticket and can never reach a response (criterion 10).
    public const string VersionProperty = "Version";

    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<Ticket> ticket = modelBuilder.Entity<Ticket>();
        ticket.ToTable("Tickets");

        // Uppercase TEXT (the SQLite provider's default for Guid), which sorts like Guid.CompareTo (criterion 6).
        ticket.HasKey(t => t.Id);
        ticket.Property(t => t.Id).ValueGeneratedNever();

        // Documentation only: SQLite doesn't enforce lengths, and the endpoint is the validator (criterion 3).
        ticket.Property(t => t.Title).HasMaxLength(TicketsEndpoints.TitleMaxLength);
        ticket.Property(t => t.Description).HasMaxLength(TicketsEndpoints.DescriptionMaxLength);

        // The canonical name, not a number, so reordering the enum can't change stored data (criterion 7).
        ticket.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);

        // UTC ticks in an INTEGER: the database can sort it (SQLite can't ORDER BY a DateTimeOffset) and it
        // round-trips to the 100 ns tick (criteria 4 and 5). The app only ever writes UTC.
        ticket.Property(t => t.CreatedAt).HasConversion(
            value => value.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

        ticket.Property<int>(VersionProperty).IsConcurrencyToken();

        // T-006 criteria 14 and 19: the creator's sub. Nullable, because tickets from before T-006 have none.
        // Indexed for the non-agent list, which filters on it (criterion 17).
        ticket.Property(t => t.CreatedBy);
        ticket.HasIndex(t => t.CreatedBy);
    }
}
