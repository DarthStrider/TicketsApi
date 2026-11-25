using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TicketsApi.Data;

namespace TicketsApi.Features.Tickets;

// T-005: tickets stored in SQLite through EF Core.
internal sealed class EfTicketStore(TicketsDbContext db, TicketStoreTestHooks testHooks) : ITicketStore
{
    public async Task AddAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        try
        {
            EntityEntry<Ticket> entry = db.Tickets.Add(ticket);
            entry.Property<int>(TicketsDbContext.VersionProperty).CurrentValue = 0; // explicit: a new ticket's version
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // Reads never track entities, so nothing stale can be served from this context later, even after a failure.
            db.ChangeTracker.Clear();
        }
    }

    public Task<Ticket?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    // Filtered and sorted by the database (T-005 criterion 5): newest first, then id descending for ties.
    public async Task<IReadOnlyList<Ticket>> ListAsync(TicketStatus? status, CancellationToken cancellationToken)
    {
        IQueryable<Ticket> query = db.Tickets.AsNoTracking();
        if (status is { } filter)
        {
            query = query.Where(t => t.Status == filter);
        }

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .ToListAsync(cancellationToken);
    }

    // Optimistic check-and-set on the version (T-005 criteria 8 and 9): read the status and version, then write
    // only if the version is unchanged. No transaction or lock is held in between, so the test hook can pause here
    // while other requests write. A version, not the status, is the token, so a change A -> B -> A in between
    // still makes this write fail.
    public async Task<Ticket?> TryUpdateStatusAsync(
        Guid id, TicketStatus expected, TicketStatus next, CancellationToken cancellationToken)
    {
        var current = await db.Tickets.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new { Ticket = t, Version = EF.Property<int>(t, TicketsDbContext.VersionProperty) })
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null || current.Ticket.Status != expected)
        {
            return null;
        }

        if (testHooks.BeforeStatusWriteAsync is { } hook)
        {
            await hook(id, cancellationToken);
        }

        int version = current.Version;
        int updated = await db.Tickets
            .Where(t => t.Id == id && EF.Property<int>(t, TicketsDbContext.VersionProperty) == version)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.Status, next)
                    .SetProperty(t => EF.Property<int>(t, TicketsDbContext.VersionProperty), version + 1),
                cancellationToken);

        if (updated == 0)
        {
            return null;
        }

        return new Ticket
        {
            Id = current.Ticket.Id,
            Title = current.Ticket.Title,
            Description = current.Ticket.Description,
            Status = next,
            CreatedAt = current.Ticket.CreatedAt,
        };
    }
}
