using System.Security.Claims;
using TicketsApi.Auth;

namespace TicketsApi.Features.Tickets;

// T-006 criteria 10 to 18: agents read and change every ticket; anyone else reads only the tickets they created.
internal sealed class TicketAccess(IConfiguration configuration) : ITicketAccess
{
    private const string AgentRole = "agent";

    // Exact and case-sensitive: "Agent" or "agents" is not an agent (criterion 11). A role claim that holds a JSON
    // array arrives as one claim per value. Nested claims such as Keycloak's realm_access.roles are not read (criterion 13).
    public bool IsAgent(ClaimsPrincipal user)
    {
        string roleClaim = AuthSetup.RoleClaim(configuration);
        return user.Claims.Any(claim => claim.Type == roleClaim && string.Equals(claim.Value, AgentRole, StringComparison.Ordinal));
    }

    // A ticket from before T-006 has no creator, so only agents can read it (criterion 18).
    public bool CanRead(ClaimsPrincipal user, Ticket ticket) =>
        IsAgent(user) || (ticket.CreatedBy is not null && ticket.CreatedBy == user.Subject());

    public string? ListCreatorFilter(ClaimsPrincipal user) => IsAgent(user) ? null : user.Subject();
}
