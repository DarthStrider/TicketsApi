using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TicketsApi.Tests.Tickets;

// Unreadable bodies, 415, the body size cap and the encoding tests are in ErrorsAndLimitsTests (real Kestrel).
public class CreateTicketTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // ---- Happy path ----

    [Fact]
    public async Task Valid_ticket_returns_201_with_location_and_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var before = DateTimeOffset.UtcNow;

        var response = await _client.PostAsJsonAsync(
            "/tickets", new { title = "Printer jammed", description = "Floor 3 printer" }, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadJsonAsync(response, ct);
        var id = body.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal("Open", body.GetProperty("status").GetString());
        Assert.Equal("Printer jammed", body.GetProperty("title").GetString());
        Assert.Equal("Floor 3 printer", body.GetProperty("description").GetString());

        var createdAt = body.GetProperty("createdAt").GetDateTimeOffset();
        Assert.InRange(createdAt, before.AddSeconds(-5), DateTimeOffset.UtcNow.AddSeconds(5));

        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"/tickets/{id}", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Location_is_exactly_tickets_slash_id()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await PostJsonAsync(new { title = "Location check" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await ReadJsonAsync(response, ct)).GetProperty("id").GetGuid();
        Assert.Equal($"/tickets/{id}", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Title_of_200_chars_is_accepted()
    {
        var response = await PostJsonAsync(new { title = new string('a', 200), description = "d" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Missing_description_is_accepted()
    {
        var response = await PostJsonAsync(new { title = "No description" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Description_of_4000_chars_is_accepted()
    {
        var response = await PostJsonAsync(new { title = "t", description = new string('d', 4000) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_fields_are_ignored()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await PostRawAsync(
            """{ "title": "t", "description": "d", "foo": 1, "status": "Closed" }""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response, ct);
        Assert.Equal("Open", body.GetProperty("status").GetString());
        Assert.False(body.TryGetProperty("foo", out _));
    }

    // ---- Title validation ----

    [Theory]
    [InlineData("""{ "title": "", "description": "d" }""")]
    [InlineData("""{ "title": "   ", "description": "d" }""")]
    [InlineData("""{ "description": "d" }""")]
    [InlineData("""{ "title": null, "description": "d" }""")]
    public async Task Title_empty_or_missing_returns_400_naming_title(string json)
    {
        var response = await PostRawAsync(json);

        await AssertValidationErrorAsync(response, "title");
    }

    [Fact]
    public async Task Title_too_long_returns_400_naming_title()
    {
        var response = await PostJsonAsync(new { title = new string('a', 201), description = "d" });

        await AssertValidationErrorAsync(response, "title");
    }

    [Fact]
    public async Task Title_of_101_emoji_is_202_units_and_returns_400_naming_title()
    {
        var title = Repeat("😀", 101);
        Assert.Equal(202, title.Length);

        var response = await PostJsonAsync(new { title });

        await AssertValidationErrorAsync(response, "title");
    }

    // ---- Trimming ----

    [Fact]
    public async Task Title_is_trimmed_before_storing()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await PostJsonAsync(new { title = "  Printer  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response, ct);
        Assert.Equal("Printer", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Title_is_trimmed_before_length_check()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await PostJsonAsync(new { title = "a" + new string(' ', 200) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response, ct);
        Assert.Equal("a", body.GetProperty("title").GetString());
    }

    // ---- Description validation ----

    [Fact]
    public async Task Description_too_long_returns_400_naming_description()
    {
        var response = await PostJsonAsync(new { title = "t", description = new string('d', 4001) });

        await AssertValidationErrorAsync(response, "description");
    }

    // ---- Helpers ----

    private Task<HttpResponseMessage> PostJsonAsync<T>(T value) =>
        _client.PostAsJsonAsync("/tickets", value, TestContext.Current.CancellationToken);

    private Task<HttpResponseMessage> PostRawAsync(string json) =>
        _client.PostAsync(
            "/tickets",
            new StringContent(json, Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

    private static string Repeat(string s, int count) => string.Concat(Enumerable.Repeat(s, count));


    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    private static async Task AssertValidationErrorAsync(HttpResponseMessage response, string field)
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await ReadJsonAsync(response, ct);
        Assert.True(body.TryGetProperty("errors", out var errors), "ProblemDetails has no 'errors'.");
        Assert.True(errors.TryGetProperty(field, out _), $"ProblemDetails errors does not name '{field}'.");
    }
}
