using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TicketsApi.Features.Tickets;
using TicketsApi.Tests.Auth;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Tickets;

// T-006 criteria 20 to 22: only agents change status, the read check runs before the change, and the check order.
public class ChangeStatusAuthTests(TicketsApiFactory factory) : IClassFixture<TicketsApiFactory>
{
    private const string Alice = "alice";

    // ---- Non-agents get 403, whatever the id and body (criterion 20) ----

    public static TheoryData<string, string, string, string> NonAgentCases => new()
    {
        // case, id kind, body, content type
        { "own ticket, valid change", "own", """{ "status": "InProgress" }""", "application/json" },
        { "own ticket, same status", "own", """{ "status": "Open" }""", "application/json" },
        { "own ticket, disallowed transition", "own", """{ "status": "Resolved" }""", "application/json" },
        { "unknown id", "unknown", """{ "status": "Closed" }""", "application/json" },
        { "non-GUID id", "abc", """{ "status": "Closed" }""", "application/json" },
        { "wrong content type", "own", """{ "status": "Closed" }""", "text/plain" },
        { "invalid body", "own", "[]", "application/json" },
        { "empty body", "own", "", "application/json" },
    };

    [Theory]
    [MemberData(nameof(NonAgentCases))]
    public async Task Non_agent_gets_403_and_the_ticket_is_unchanged(string scenario, string idKind, string json, string contentType)
    {
        _ = scenario;
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        HttpClient alice = app.CreateClientAs(Alice);
        Guid own = (await alice.CreateTicketAsync("Mine")).GetProperty("id").GetGuid();
        string id = idKind switch { "own" => own.ToString(), "unknown" => Guid.NewGuid().ToString(), _ => idKind };

        HttpResponseMessage response = await alice.PatchStatusRawAsync(id, json, contentType);

        await AssertProblemDetailsAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("Open", await TicketRows.StatusAsync(app, own));
    }

    [Fact]
    public async Task Agent_can_change_status()
    {
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        Guid id = (await app.CreateClientAs(Alice).CreateTicketAsync("Alice's")).GetProperty("id").GetGuid();

        HttpResponseMessage response = await app.CreateClientAs("carol;agent").PatchStatusAsync(id, new { status = "InProgress" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- PATCH uses the shared read check (criterion 21) ----

    [Theory]
    [InlineData("""{ "status": "InProgress" }""")] // would be 200
    [InlineData("""{ "status": "Open" }""")] // same status: would be 200 with the full ticket
    [InlineData("""{ "status": "Resolved" }""")] // would be 409 with currentStatus
    public async Task Agent_gets_404_for_a_ticket_the_read_check_denies(string json)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITicketAccess>();
            services.AddSingleton<ITicketAccess, DenyReadAccess>();
        }));
        HttpClient agent = app.CreateClient(); // the default test user is an agent
        Guid id = (await agent.CreateTicketAsync("Hidden")).GetProperty("id").GetGuid();

        HttpResponseMessage response = await agent.PatchStatusRawAsync(id.ToString(), json);

        JsonElement body = await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound);
        Assert.False(body.TryGetProperty("currentStatus", out _));
        Assert.DoesNotContain("Hidden", await response.Content.ReadAsStringAsync(ct));
        Assert.Equal("Open", await TicketRows.StatusAsync(app, id));
    }

    // ---- Check order (criterion 22) ----

    public static TheoryData<string, string, string, string, string, HttpStatusCode> OrderCases => new()
    {
        // case, user, id, body, content type, expected
        { "401 before 403", TestAuthHandler.Anonymous, "unknown", """{ "status": "Closed" }""", "application/json", HttpStatusCode.Unauthorized },
        { "401 before route 404", TestAuthHandler.Anonymous, "abc", """{ "status": "Closed" }""", "application/json", HttpStatusCode.Unauthorized },
        { "403 before route 404", Alice, "abc", "[]", "text/plain", HttpStatusCode.Forbidden },
        { "403 before 415", Alice, "unknown", """{ "status": "Closed" }""", "text/plain", HttpStatusCode.Forbidden },
        { "403 before 400", Alice, "unknown", "[]", "application/json", HttpStatusCode.Forbidden },
        { "route 404 before 415", "carol;agent", "abc", """{ "status": "Closed" }""", "text/plain", HttpStatusCode.NotFound },
        { "route 404 before 400", "carol;agent", "abc", "[]", "application/json", HttpStatusCode.NotFound },
        { "415 before lookup 404", "carol;agent", "unknown", """{ "status": "Closed" }""", "text/plain", HttpStatusCode.UnsupportedMediaType },
        { "400 before lookup 404", "carol;agent", "unknown", "[]", "application/json", HttpStatusCode.BadRequest },
    };

    [Theory]
    [MemberData(nameof(OrderCases))]
    public async Task Checks_run_in_order(string scenario, string user, string id, string json, string contentType, HttpStatusCode expected)
    {
        _ = scenario;
        string target = id == "unknown" ? Guid.NewGuid().ToString() : id;

        HttpResponseMessage response = await factory.CreateClientAs(user).PatchStatusRawAsync(target, json, contentType);

        await AssertProblemDetailsAsync(response, expected);
    }

    // On POST, 401 comes before 415 and 400 (413 is checked on Kestrel in ErrorsAndLimitsTests).
    [Theory]
    [InlineData("title=t", "text/plain")]
    [InlineData("{", "application/json")]
    public async Task Post_returns_401_before_415_and_400(string body, string contentType)
    {
        HttpResponseMessage response = await factory.CreateClientAs(TestAuthHandler.Anonymous).PostAsync(
            "/tickets", new StringContent(body, Encoding.UTF8, contentType), TestContext.Current.CancellationToken);

        await AssertProblemDetailsAsync(response, HttpStatusCode.Unauthorized);
    }

    // Lets the agent in, but denies reading every ticket.
    private sealed class DenyReadAccess : ITicketAccess
    {
        public bool IsAgent(ClaimsPrincipal user) => true;

        public bool CanRead(ClaimsPrincipal user, Ticket ticket) => false;

        public string? ListCreatorFilter(ClaimsPrincipal user) => null;
    }
}
