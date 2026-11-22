using System.Collections.Concurrent;

namespace TicketsApi.Features.Tickets;

// Temporary storage until EF Core arrives in T-005.
internal sealed class InMemoryTicketStore : ITicketStore
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
}
