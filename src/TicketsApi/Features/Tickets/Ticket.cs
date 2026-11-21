using System.Text.Json.Serialization;

namespace TicketsApi.Features.Tickets;

[JsonConverter(typeof(JsonStringEnumConverter<TicketStatus>))]
public enum TicketStatus
{
    Open,
}

internal sealed class Ticket
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required TicketStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
