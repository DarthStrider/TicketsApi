using Microsoft.AspNetCore.Authorization;
using TicketsApi.Features.Tickets;

namespace TicketsApi.Auth;

// The Agent policy's requirement (T-006 criterion 20): the caller must be an agent.
internal sealed class AgentRequirement : IAuthorizationRequirement;

// Decides the requirement with ITicketAccess, so "who is an agent" has one owner, and a test host that replaces
// ITicketAccess also changes who passes the policy (criterion 21).
internal sealed class AgentAuthorizationHandler(ITicketAccess access) : AuthorizationHandler<AgentRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AgentRequirement requirement)
    {
        if (access.IsAgent(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
