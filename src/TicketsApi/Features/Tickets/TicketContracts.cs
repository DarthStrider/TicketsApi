namespace TicketsApi.Features.Tickets;

public sealed record CreateTicketRequest(string? Title, string? Description);

public sealed record TicketResponse(
    Guid Id,
    string Title,
    string? Description,
    TicketStatus Status,
    DateTimeOffset CreatedAt);
