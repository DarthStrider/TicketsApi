namespace TicketsApi.Features.Tickets;

// The allowed status changes (T-003 criterion 4). Changing to the current status is a no-op, not a transition.
internal static class TicketTransitions
{
    private static readonly Dictionary<TicketStatus, TicketStatus[]> Allowed = new()
    {
        [TicketStatus.Open] = [TicketStatus.InProgress, TicketStatus.Closed],
        [TicketStatus.InProgress] = [TicketStatus.Resolved],
        [TicketStatus.Resolved] = [TicketStatus.Closed, TicketStatus.InProgress, TicketStatus.InReview],
        [TicketStatus.InReview] = [TicketStatus.Closed, TicketStatus.InProgress],
        [TicketStatus.Closed] = [],
    };

    public static bool IsAllowed(TicketStatus from, TicketStatus to) => Allowed[from].Contains(to);
}
