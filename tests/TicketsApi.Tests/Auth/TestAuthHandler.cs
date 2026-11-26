using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using TicketsApi.Auth;

namespace TicketsApi.Tests.Auth;

// The test authentication scheme (T-006 criterion 24). It exists only in the test project, so nothing in src/ can
// register it, and no configuration or header can turn it on in a real host.
//
// Every request is signed in as DefaultUser, an agent, so tests written before T-006 keep working unchanged
// (criterion 25). The X-Test-User header picks another caller:
//   "anonymous"        no user (the request is not authenticated)
//   "alice"            user alice with no roles
//   "alice;agent,x"    user alice with the roles agent and x
// Roles go under the configured role claim, read the same way as the API reads it.
internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string Header = "X-Test-User";
    public const string DefaultUser = "test-agent;agent";
    public const string Anonymous = "anonymous";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string user = Request.Headers.TryGetValue(Header, out StringValues value) ? value.ToString() : DefaultUser;
        if (user == Anonymous)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string[] parts = user.Split(';', 2);
        if (parts[0].Length == 0)
        {
            // Like a real token without a sub (criterion 4).
            return Task.FromResult(AuthenticateResult.Fail("The test user has no sub."));
        }

        string roleClaim = AuthSetup.RoleClaim(configuration);
        List<Claim> claims = [new(ClaimsPrincipalExtensions.SubjectClaim, parts[0])];
        if (parts.Length == 2)
        {
            claims.AddRange(parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(role => new Claim(roleClaim, role)));
        }

        ClaimsIdentity identity = new(claims, SchemeName, ClaimsPrincipalExtensions.SubjectClaim, roleClaim);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
