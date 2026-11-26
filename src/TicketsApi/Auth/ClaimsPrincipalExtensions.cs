using System.Security.Claims;

namespace TicketsApi.Auth;

internal static class ClaimsPrincipalExtensions
{
    // The claim that identifies a user (T-006 criterion 4). Inbound claims aren't renamed (MapInboundClaims = false),
    // so it keeps the JWT name.
    public const string SubjectClaim = "sub";

    // The caller's identity. Every authenticated caller has a non-empty `sub`, because a token without one is rejected
    // when it is validated, so a missing one here is a bug, not bad input.
    public static string Subject(this ClaimsPrincipal user) =>
        user.FindFirst(SubjectClaim)?.Value is { Length: > 0 } sub
            ? sub
            : throw new InvalidOperationException("An authenticated user without a sub claim reached a ticket endpoint.");
}
