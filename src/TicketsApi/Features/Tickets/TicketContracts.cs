namespace TicketsApi.Features.Tickets;

public sealed record CreateTicketRequest(string? Title, string? Description);

public sealed record TicketResponse(
    Guid Id,
    string Title,
    string? Description,
    TicketStatus Status,
    DateTimeOffset CreatedAt);

// An object, not a bare array, so paging fields (T-008) can be added without breaking clients.
public sealed record TicketListResponse(IReadOnlyList<TicketResponse> Items);
