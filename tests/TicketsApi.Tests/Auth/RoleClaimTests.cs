using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Auth;

// T-006 criteria 10 to 13, with real tokens. Whether the caller is an agent shows on PATCH of an unknown ticket:
// an agent passes the role check and gets the lookup 404; anyone else gets 403 first (criteria 20 and 22).
public class RoleClaimTests(JwtFactory factory) : IClassFixture<JwtFactory>
{
    private const string CustomClaim = "https://tickets.example/roles";

    public static TheoryData<string, bool> DefaultClaimCases => new()
    {
        { "single agent", true },
        { "array with agent", true },
        { "array without agent", false },
        { "Agent (case)", false },
        { "agents", false },
        { "no role", false },
        { "nested only", false },
        { "custom name only", false },
    };

    [Theory]
    [MemberData(nameof(DefaultClaimCases))]
    public async Task Role_claim_role_decides_who_is_an_agent(string scenario, bool isAgent)
    {
        Dictionary<string, object> claims = scenario switch
        {
            "single agent" => new() { ["role"] = "agent" },
            "array with agent" => new() { ["role"] = new List<string> { "support", "agent" } },
            "array without agent" => new() { ["role"] = new List<string> { "support", "viewer" } },
            "Agent (case)" => new() { ["role"] = "Agent" },
            "agents" => new() { ["role"] = "agents" },
            "no role" => new(),
            // Keycloak's default shape: not read (criterion 13).
            "nested only" => new() { ["realm_access"] = new Dictionary<string, object> { ["roles"] = new List<string> { "agent" } } },
            "custom name only" => new() { [CustomClaim] = "agent" },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        await AssertAgentAsync(factory, claims, isAgent);
    }

    // Criterion 12: the configured name is used exactly, and the default name is then ignored.
    [Theory]
    [InlineData(CustomClaim, true)]
    [InlineData("role", false)]
    [InlineData("groups", false)]
    public async Task Configured_role_claim_name_is_used(string claimInToken, bool isAgent)
    {
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:RoleClaim"] = CustomClaim })));

        await AssertAgentAsync(app, new Dictionary<string, object> { [claimInToken] = new List<string> { "agent" } }, isAgent);
    }

    // Okta's usual claim name (criterion 10).
    [Fact]
    public async Task Groups_claim_works_when_configured()
    {
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:RoleClaim"] = "groups" })));

        await AssertAgentAsync(app, new Dictionary<string, object> { ["groups"] = "agent" }, isAgent: true);
    }

    private async Task AssertAgentAsync(WebApplicationFactory<Program> host, Dictionary<string, object> claims, bool isAgent)
    {
        HttpClient client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Create(factory, claims: claims));

        HttpResponseMessage response = await client.PatchStatusAsync(Guid.NewGuid(), new { status = "Closed" });

        await AssertProblemDetailsAsync(response, isAgent ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden);
    }
}
