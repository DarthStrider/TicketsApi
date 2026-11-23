using System.Collections.Concurrent;

namespace TicketsApi.Features.Tickets;

// Temporary storage until EF Core arrives in T-005.
internal sealed class InMemoryTicketStore(TicketStoreTestHooks testHooks) : ITicketStore
{
    private readonly ConcurrentDictionary<Guid, Ticket> _tickets = new();

    public Task AddAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_tickets.TryAdd(ticket.Id, ticket))
        {
            throw new InvalidOperationException($"A ticket with id {ticket.Id} already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<Ticket?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_tickets.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<Ticket>> ListAsync(TicketStatus? status, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<Ticket> tickets = _tickets.Values
            .Where(t => status is null || t.Status == status)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .ToList();

        return Task.FromResult(tickets);
    }

    public async Task<Ticket?> TryUpdateStatusAsync(
        Guid id, TicketStatus expected, TicketStatus next, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_tickets.TryGetValue(id, out var current) || current.Status != expected)
        {
            return null;
        }

        var updated = new Ticket
        {
            Id = current.Id,
            Title = current.Title,
            Description = current.Description,
            Status = next,
            CreatedAt = current.CreatedAt,
        };

        if (testHooks.BeforeStatusWriteAsync is { } hook)
        {
            await hook(id, cancellationToken);
        }

        // Compare-and-swap: only replaces the exact ticket read above, so a change made in between wins and this
        // one fails. Ticket is immutable and has no Equals override, so reference equality means "unchanged since
        // read" (it even catches a change A -> B -> A).
        return _tickets.TryUpdate(id, updated, current) ? updated : null;
    }
}
