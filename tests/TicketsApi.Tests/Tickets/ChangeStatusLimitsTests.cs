using System.Net;
using System.Net.Http.Json;
using System.Text;
using TicketsApi.Features.Tickets;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Tickets;

// T-003 criteria 20 and 21 on real Kestrel (body size limits are enforced), in Development and Production,
// plus a few of the main error paths, to show Production behaves the same.
public class DevelopmentChangeStatusLimitsTests(KestrelFactory factory)
    : ChangeStatusLimitsTests<KestrelFactory>(factory);

public class ProductionChangeStatusLimitsTests(ProductionKestrelFactory factory)
    : ChangeStatusLimitsTests<ProductionKestrelFactory>(factory);

public abstract class ChangeStatusLimitsTests<TFactory> : IClassFixture<TFactory>
    where TFactory : KestrelFactory
{
    private const int Cap = (int)TicketsEndpoints.MaxRequestBodyBytes;
    private readonly HttpClient _client;

    protected ChangeStatusLimitsTests(TFactory factory)
    {
        factory.StartServer();
        _client = factory.CreateClient();
    }

    // ---- Unreadable bodies and content type (AC20) ----

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""[{ "status": "InProgress" }]""")]
    [InlineData("""{ "status": """)]
    [InlineData("""{ "status": "InProgress" """)]
    public async Task Unreadable_body_returns_400_problem_details_and_leaves_ticket_unchanged(string json)
    {
        var id = await NewTicketIdAsync();
        var before = await GetRawAsync(id);

        var response = await _client.PatchStatusRawAsync(id, json);

        await AssertValidationErrorAsync(response, "body");
        Assert.Equal(before, await GetRawAsync(id)); // AC11
    }

    // The content type is checked before the body is read, so a non-JSON type gets 415 whatever the body is,
    // including "+json" types that Minimal APIs would otherwise accept.
    [Theory]
    [InlineData("text/plain", """{ "status": "InProgress" }""")]
    [InlineData("application/xml", """{ "status": "InProgress" }""")]
    [InlineData("text/json", """{ "status": "InProgress" }""")]
    [InlineData("application/merge-patch+json", """{ "status": "InProgress" }""")]
    [InlineData("application/merge-patch+json", "")]
    [InlineData("application/merge-patch+json", "{")]
    [InlineData("application/json-patch+json", """[{ "op": "replace", "path": "/status", "value": "Closed" }]""")]
    public async Task Non_json_content_type_returns_415_and_leaves_ticket_unchanged(string contentType, string body)
    {
        var id = await NewTicketIdAsync();
        var before = await GetRawAsync(id);

        var response = await _client.PatchStatusRawAsync(id, body, contentType);

        await AssertProblemDetailsAsync(response, HttpStatusCode.UnsupportedMediaType);
        Assert.Equal(before, await GetRawAsync(id)); // AC11
    }

    [Fact]
    public async Task Missing_content_type_returns_415()
    {
        var id = await NewTicketIdAsync();
        var content = new StringContent("""{ "status": "InProgress" }""");
        content.Headers.ContentType = null;

        var response = await _client.PatchAsync($"/tickets/{id}/status", content, TestContext.Current.CancellationToken);

        await AssertProblemDetailsAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Theory]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("APPLICATION/JSON")]
    public async Task Json_content_type_variants_are_accepted(string contentType)
    {
        var id = await NewTicketIdAsync();

        var response = await _client.PatchStatusRawAsync(id, """{ "status": "InProgress" }""", contentType);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- The main error paths also behave the same in Production (AC8, AC13-15, AC23, AC27) ----

    [Fact]
    public async Task Disallowed_transition_returns_409_with_extensions()
    {
        var id = await NewTicketIdAsync();

        var response = await _client.PatchStatusRawAsync(id, """{ "status": "Resolved" }""");

        var problem = await AssertProblemDetailsAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("Open", problem.GetProperty("currentStatus").GetString());
        Assert.Equal("Resolved", problem.GetProperty("requestedStatus").GetString());
    }

    [Theory]
    [InlineData("""{ "status": 1 }""")]
    [InlineData("""{ "status": "Banana" }""")]
    [InlineData("""{ "status": "InProgress", "status": "Closed" }""")]
    public async Task Invalid_status_returns_400_naming_status(string json)
    {
        var id = await NewTicketIdAsync();

        var response = await _client.PatchStatusRawAsync(id, json);

        await AssertValidationErrorAsync(response, "status");
    }

    [Fact]
    public async Task Non_guid_id_returns_404()
    {
        var response = await _client.PatchStatusRawAsync("abc", """{ "status": "InProgress" }""");

        await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound);
    }

    // ---- Body size cap (AC21) ----

    [Fact]
    public async Task Body_one_byte_over_the_cap_returns_413_problem_details()
    {
        var id = await NewTicketIdAsync();

        var response = await OverCapRequests.SendAsync(
            _client, HttpMethod.Patch, $"/tickets/{id}/status", ValidBodyOfSize(Cap + 1));

        await AssertProblemDetailsAsync(response, HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Body_exactly_at_the_cap_is_accepted()
    {
        var id = await NewTicketIdAsync();

        var response = await _client.PatchStatusRawAsync(id, ValidBodyOfSize(Cap));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Helpers ----

    private async Task<string> NewTicketIdAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/tickets", new { title = "t" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response, TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetGuid().ToString();
    }

    private async Task<string> GetRawAsync(string id)
    {
        var ct = TestContext.Current.CancellationToken;
        return await (await _client.GetAsync($"/tickets/{id}", ct)).Content.ReadAsStringAsync(ct);
    }

    // A valid status change padded with an ignored unknown field to exactly `bytes` bytes of UTF-8.
    private static string ValidBodyOfSize(int bytes)
    {
        const string prefix = "{\"status\":\"InProgress\",\"pad\":\"";
        const string suffix = "\"}";
        var json = prefix + new string('x', bytes - prefix.Length - suffix.Length) + suffix;
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(json));
        return json;
    }
}
