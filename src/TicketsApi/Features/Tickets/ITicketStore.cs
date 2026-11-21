namespace TicketsApi.Features.Tickets;

internal interface ITicketStore
{
    Task AddAsync(Ticket ticket, CancellationToken cancellationToken);
}
