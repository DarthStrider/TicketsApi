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
}
