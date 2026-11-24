using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Tickets;

// T-002 criteria 4-17. Every test gets its own host (WithClock), so the store starts empty.
public class ListTicketsTests(TicketsApiFactory factory)
    : IClassFixture<TicketsApiFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // ---- List ----

    [Fact]
    public async Task List_returns_items_sorted_by_created_at_newest_first()
    {
        TestClock clock = new(T0);
        using WebApplicationFactory<Program> app = factory.WithClock(clock);
        HttpClient client = app.CreateClient();

        // Created out of time order, so the id order (v7, creation order) differs from the createdAt order.
        clock.UtcNow = T0.AddMinutes(2);
        JsonElement newest = await client.CreateTicketAsync("newest");
        clock.UtcNow = T0;
        JsonElement oldest = await client.CreateTicketAsync("oldest");
        clock.UtcNow = T0.AddMinutes(1);
        JsonElement middle = await client.CreateTicketAsync("middle");

        List<Guid> ids = await GetItemIdsAsync(client, "/tickets");

        Assert.Equal([Id(newest), Id(middle), Id(oldest)], ids);
    }

    [Fact]
    public async Task List_body_is_an_object_with_only_items()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> app = factory.WithClock(new TestClock(T0));
        HttpClient client = app.CreateClient();
        const string title = "Imprimante bloquée 🖨️";
        const string description = "Café renversé ☕";
        JsonElement created = await client.CreateTicketAsync(title, description);

        HttpResponseMessage response = await client.GetAsync("/tickets", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ReadJsonAsync(response, ct);
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        Assert.Equal(["items"], body.EnumerateObject().Select(p => p.Name));

        // Each item has the same shape as the POST 201 body.
        JsonElement item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(created.GetRawText(), item.GetRawText());

        // Guards against a vacuous match where both bodies lost the values.
        Assert.Equal(title, item.GetProperty("title").GetString());
        Assert.Equal(description, item.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Same_created_at_is_ordered_by_id_descending()
    {
        using WebApplicationFactory<Program> app = factory.WithClock(new TestClock(T0));
        HttpClient client = app.CreateClient();
        List<Guid> created = new();
        for (int i = 0; i < 5; i++)
        {
            created.Add(Id(await client.CreateTicketAsync($"tie {i}")));
        }

        List<Guid> ids = await GetItemIdsAsync(client, "/tickets");

        Assert.Equal(created.OrderByDescending(id => id), ids);
    }

    // AC7: no paging. 101 is more than any plausible default page size (for example 50 or 100).
    [Fact]
    public async Task List_returns_every_ticket_without_paging()
    {
        const int count = 101;
        TestClock clock = new(T0);
        using WebApplicationFactory<Program> app = factory.WithClock(clock);
        HttpClient client = app.CreateClient();
        List<Guid> created = new();
        for (int i = 0; i < count; i++)
        {
            clock.UtcNow = T0.AddSeconds(i);
            created.Add(Id(await client.CreateTicketAsync($"ticket {i}")));
        }

        List<Guid> ids = await GetItemIdsAsync(client, "/tickets");

        Assert.Equal(count, ids.Count);
        Assert.Equal(Enumerable.Reverse(created), ids);
    }

    [Fact]
    public async Task Empty_store_returns_200_with_empty_items()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> app = factory.WithClock(new TestClock(T0));
        HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/tickets", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ReadJsonAsync(response, ct);
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("/tickets?foo=1")]
    [InlineData("/tickets?sort=asc&page=2&pageSize=1")]
    public async Task Unknown_query_parameter_is_ignored(string url)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        TestClock clock = new(T0);
        using WebApplicationFactory<Program> app = factory.WithClock(clock);
        HttpClient client = app.CreateClient();
        await client.CreateTicketAsync("a");
        clock.UtcNow = T0.AddMinutes(1);
        await client.CreateTicketAsync("b");

        string expected = await (await client.GetAsync("/tickets", ct)).Content.ReadAsStringAsync(ct);
        HttpResponseMessage response = await client.GetAsync(url, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await response.Content.ReadAsStringAsync(ct));
    }

    // ---- Filter by status ----

    // While Open is the only status, this checks matching tickets come back in order.
    // The test that other statuses are excluded is added in T-003.
    [Theory]
    [InlineData("Open")]
    [InlineData("open")]
    [InlineData("OPEN")]
    public async Task Status_filter_returns_matching_tickets_newest_first(string status)
    {
        TestClock clock = new(T0);
        using WebApplicationFactory<Program> app = factory.WithClock(clock);
        HttpClient client = app.CreateClient();
        JsonElement older = await client.CreateTicketAsync("older");
        clock.UtcNow = T0.AddMinutes(1);
        JsonElement newer = await client.CreateTicketAsync("newer");

        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpResponseMessage response = await client.GetAsync($"/tickets?status={status}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<JsonElement> items = (await ReadJsonAsync(response, ct)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([Id(newer), Id(older)], items.Select(Id));

        // AC11: the response always uses the canonical name, whatever case the request used.
        Assert.All(items, item => Assert.Equal("Open", item.GetProperty("status").GetString()));
    }

    // T-003 criterion 26 (deferred from T-002 criterion 9): each status returns only its own tickets, in order.
    [Theory]
    [InlineData("Open", "Open")]
    [InlineData("InProgress", "InProgress")]
    [InlineData("Resolved", "Resolved")]
    [InlineData("InReview", "InReview")]
    [InlineData("Closed", "Closed")]
    [InlineData("inreview", "InReview")]
    [InlineData("inprogress", "InProgress")]
    public async Task Status_filter_excludes_other_statuses(string query, string status)
    {
        TestClock clock = new(T0);
        using WebApplicationFactory<Program> app = factory.WithClock(clock);
        HttpClient client = app.CreateClient();

        // Two tickets per status, created one minute apart, so the expected order is newest first.
        Dictionary<string, List<Guid>> byStatus = new();
        int minute = 0;
        foreach (string s in new[] { "Open", "InProgress", "Resolved", "InReview", "Closed" })
        {
            byStatus[s] = [];
            for (int i = 0; i < 2; i++)
            {
                clock.UtcNow = T0.AddMinutes(minute++);
                byStatus[s].Add(await client.CreateTicketInStatusAsync(s, $"{s} {i}"));
            }
        }

        List<Guid> ids = await GetItemIdsAsync(client, $"/tickets?status={query}");

        Assert.Equal(Enumerable.Reverse(byStatus[status]), ids);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("-1")]
    public async Task Numeric_status_returns_400(string status) =>
        await AssertStatusRejectedAsync($"/tickets?status={status}");

    [Theory]
    [InlineData("Banana")]
    [InlineData("Opened")]
    [InlineData("Open,Open")]
    [InlineData("Öpen")]
    public async Task Unknown_status_name_returns_400(string status) =>
        await AssertStatusRejectedAsync($"/tickets?status={status}");

    [Fact]
    public async Task Empty_status_returns_400() =>
        await AssertStatusRejectedAsync("/tickets?status=");

    [Theory]
    [InlineData("/tickets?status=Open&status=Open")]
    [InlineData("/tickets?status=Open&status=")]
    public async Task Repeated_status_returns_400(string url) =>
        await AssertStatusRejectedAsync(url);

    // "+" in a query string decodes to a space, so "+Open" is " Open".
    [Theory]
    [InlineData("%20Open")]
    [InlineData("Open%20")]
    [InlineData("%20")]
    [InlineData("+Open")]
    public async Task Status_with_surrounding_spaces_returns_400(string status) =>
        await AssertStatusRejectedAsync($"/tickets?status={status}");

    // ---- Helpers ----

    private async Task AssertStatusRejectedAsync(string url)
    {
        using WebApplicationFactory<Program> app = factory.WithClock(new TestClock(T0));
        HttpClient client = app.CreateClient();
        await client.CreateTicketAsync("exists");

        HttpResponseMessage response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        await AssertValidationErrorAsync(response, "status");
    }

    private static Guid Id(JsonElement ticket) => ticket.GetProperty("id").GetGuid();

    private static async Task<List<Guid>> GetItemIdsAsync(HttpClient client, string url)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpResponseMessage response = await client.GetAsync(url, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ReadJsonAsync(response, ct);
        return body.GetProperty("items").EnumerateArray().Select(Id).ToList();
    }
}
