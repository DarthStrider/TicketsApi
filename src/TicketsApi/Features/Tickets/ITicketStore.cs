namespace TicketsApi.Features.Tickets;

internal interface ITicketStore
{
    Task AddAsync(Ticket ticket, CancellationToken cancellationToken);

    Task<Ticket?> GetAsync(Guid id, CancellationToken cancellationToken);

    // Newest first: createdAt descending, then id descending for ties (T-002 criteria 4 and 5).
    Task<IReadOnlyList<Ticket>> ListAsync(TicketStatus? status, CancellationToken cancellationToken);

    // Atomic check-and-set (T-003 criterion 24): sets the status to `next` only if it is still `expected`.
    // Returns the updated ticket, or null if the ticket is missing or its status is no longer `expected`.
    Task<Ticket?> TryUpdateStatusAsync(
        Guid id, TicketStatus expected, TicketStatus next, CancellationToken cancellationToken);
}
