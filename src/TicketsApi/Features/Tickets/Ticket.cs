using System.Text.Json.Serialization;

namespace TicketsApi.Features.Tickets;

[JsonConverter(typeof(JsonStringEnumConverter<TicketStatus>))]
public enum TicketStatus
{
    Open,
    InProgress,
    Resolved,
    InReview,
    Closed,
}

// Immutable on purpose: a status change writes a new row version and returns a new Ticket, never mutates one.
// "Unchanged since read" is decided by the database's Version column (T-005 criterion 8), which is deliberately
// not a property here, so it can never reach a response.
internal sealed class Ticket
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required TicketStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    // The creator's `sub` (T-006 criterion 14), which decides who may read the ticket. Null for tickets created
    // before T-006 (criterion 18). Never in a response: ToResponse doesn't map it (criterion 19).
    public string? CreatedBy { get; init; }
}
