namespace TicketsApi.Features.Tickets;

internal interface ITicketStore
{
    Task AddAsync(Ticket ticket, CancellationToken cancellationToken);

    Task<Ticket?> GetAsync(Guid id, CancellationToken cancellationToken);

    // Newest first: createdAt descending, then id descending for ties (T-002 criteria 4 and 5).
    Task<IReadOnlyList<Ticket>> ListAsync(TicketStatus? status, CancellationToken cancellationToken);
}
