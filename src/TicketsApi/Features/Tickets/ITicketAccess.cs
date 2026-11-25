using System.Security.Claims;

namespace TicketsApi.Features.Tickets;

// Who may do what with tickets (T-006). The one place that owns the read rule, used by GET, the list and PATCH
// (criterion 21), so a test host can replace it.
internal interface ITicketAccess
{
    // True when the caller's role claim (Authentication:RoleClaim) holds exactly "agent" (criteria 10 to 13).
    bool IsAgent(ClaimsPrincipal user);

    // Agents read every ticket; anyone else only the tickets they created (criteria 15, 16 and 18).
    bool CanRead(ClaimsPrincipal user, Ticket ticket);

    // The creator to filter the list by: null for an agent (all tickets), otherwise the caller's sub (criterion 17).
    string? ListCreatorFilter(ClaimsPrincipal user);
}
