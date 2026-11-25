using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TicketsApi.Tests.Auth;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Tickets;

// T-006 criteria 14 to 19: who creates, reads and lists which tickets. Each test uses its own host, so its database
// holds only the tickets it creates.
public class TicketAccessTests(TicketsApiFactory factory) : IClassFixture<TicketsApiFactory>
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string Agent = "carol;agent";

    // ---- Create (criterion 14) ----

    [Theory]
    [InlineData(Alice, "alice")]
    [InlineData(Agent, "carol")]
    public async Task Creator_is_stored(string user, string sub)
    {
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });

        Guid id = (await app.CreateClientAs(user).CreateTicketAsync("Printer")).GetProperty("id").GetGuid();

        Assert.Equal(sub, await TicketRows.CreatedByAsync(app, id));
    }

    // ---- Read one (criteria 15 and 16) ----

    [Fact]
    public async Task Non_agent_reads_own_ticket()
    {
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        HttpClient alice = app.CreateClientAs(Alice);
        Guid id = (await alice.CreateTicketAsync("Mine")).GetProperty("id").GetGuid();

        HttpResponseMessage response = await alice.GetAsync($"/tickets/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Someone else's ticket is indistinguishable from an unknown id: same status, same title, same body shape.
    [Fact]
    public async Task Non_agent_gets_404_for_someone_elses_ticket_like_an_unknown_id()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        Guid bobs = (await app.CreateClientAs(Bob).CreateTicketAsync("Bob's")).GetProperty("id").GetGuid();
        HttpClient alice = app.CreateClientAs(Alice);

        HttpResponseMessage other = await alice.GetAsync($"/tickets/{bobs}", ct);
        HttpResponseMessage unknown = await alice.GetAsync($"/tickets/{Guid.NewGuid()}", ct);

        JsonElement otherBody = await AssertProblemDetailsAsync(other, HttpStatusCode.NotFound);
        JsonElement unknownBody = await AssertProblemDetailsAsync(unknown, HttpStatusCode.NotFound);
        Assert.Equal(unknownBody.GetProperty("title").GetString(), otherBody.GetProperty("title").GetString());
        Assert.Equal(Names(unknownBody), Names(otherBody));
        Assert.DoesNotContain("Bob's", await other.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task Agent_reads_any_ticket()
    {
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        Guid id = (await app.CreateClientAs(Alice).CreateTicketAsync("Alice's")).GetProperty("id").GetGuid();

        HttpResponseMessage response = await app.CreateClientAs(Agent).GetAsync($"/tickets/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- List (criterion 17) ----

    [Theory]
    [InlineData("")]
    [InlineData("?status=Open")]
    public async Task Non_agent_lists_only_own_tickets_newest_first(string query)
    {
        using WebApplicationFactory<Program> app = factory.WithClock(new TestClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        TestClock clock = (TestClock)app.Services.GetRequiredService<TimeProvider>();
        HttpClient alice = app.CreateClientAs(Alice);
        HttpClient bob = app.CreateClientAs(Bob);
        Guid a1 = await CreateAtAsync(alice, clock, "a1", 1);
        await CreateAtAsync(bob, clock, "b1", 2);
        Guid a2 = await CreateAtAsync(alice, clock, "a2", 3);

        List<Guid> ids = await ListIdsAsync(alice, query);

        Assert.Equal([a2, a1], ids);
    }

    [Fact]
    public async Task Agent_lists_all_tickets()
    {
        using WebApplicationFactory<Program> app = factory.WithClock(new TestClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        TestClock clock = (TestClock)app.Services.GetRequiredService<TimeProvider>();
        Guid a1 = await CreateAtAsync(app.CreateClientAs(Alice), clock, "a1", 1);
        Guid b1 = await CreateAtAsync(app.CreateClientAs(Bob), clock, "b1", 2);

        List<Guid> ids = await ListIdsAsync(app.CreateClientAs(Agent), "");

        Assert.Equal([b1, a1], ids);
    }

    // ---- Tickets from before T-006 (criterion 18) ----

    [Fact]
    public async Task Ticket_without_creator_is_visible_only_to_agents()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        Guid old = Guid.NewGuid();
        await TicketRows.InsertWithoutCreatorAsync(app, old, "Old");
        HttpClient alice = app.CreateClientAs(Alice);
        HttpClient agent = app.CreateClientAs(Agent);

        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/tickets/{old}", ct)).StatusCode);
        Assert.DoesNotContain(old, await ListIdsAsync(alice, ""));
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync($"/tickets/{old}", ct)).StatusCode);
        Assert.Contains(old, await ListIdsAsync(agent, ""));
    }

    // ---- The creator never appears in a response (criterion 19) ----

    [Fact]
    public async Task Creator_never_appears_in_responses()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        const string sub = "unique-sub-7f3a9c";
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        HttpClient owner = app.CreateClientAs($"{sub};agent");

        HttpResponseMessage created = await owner.PostAsync(
            "/tickets", new StringContent("""{ "title": "t" }""", Encoding.UTF8, "application/json"), ct);
        Guid id = (await ReadJsonAsync(created, ct)).GetProperty("id").GetGuid();
        HttpResponseMessage[] responses =
        [
            created,
            await owner.GetAsync($"/tickets/{id}", ct),
            await owner.GetAsync("/tickets", ct),
            await owner.PatchStatusAsync(id, new { status = "InProgress" }),
        ];

        foreach (HttpResponseMessage response in responses)
        {
            Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {response.StatusCode}");
            string body = await response.Content.ReadAsStringAsync(ct);
            Assert.DoesNotContain(sub, body);
            Assert.DoesNotContain("createdBy", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(response.Headers.Concat(response.Content.Headers), h => h.Value.Any(v => v.Contains(sub)));
        }
    }

    // ---- Helpers ----

    private static async Task<Guid> CreateAtAsync(HttpClient client, TestClock clock, string title, int minute)
    {
        clock.UtcNow = DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddMinutes(minute);
        return (await client.CreateTicketAsync(title)).GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> ListIdsAsync(HttpClient client, string query)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpResponseMessage response = await client.GetAsync($"/tickets{query}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await ReadJsonAsync(response, ct)).GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetGuid())];
    }

    private static List<string> Names(JsonElement body) => [.. body.EnumerateObject().Select(p => p.Name).Where(n => n != "traceId").Order()];

}
