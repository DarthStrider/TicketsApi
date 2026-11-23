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

// Immutable on purpose: a status change stores a new Ticket. InMemoryTicketStore's compare-and-swap relies on reference
// identity meaning "unchanged since read" (T-003 criterion 24), so don't add setters or override Equals,
// and don't make this a record.
internal sealed class Ticket
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required TicketStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
