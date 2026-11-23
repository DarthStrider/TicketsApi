using System.Net;
using System.Text;
using System.Text.Json;
using TicketsApi.Features.Tickets;

namespace TicketsApi.Tests.Tickets;

// Runs on real Kestrel (body size limits are enforced) in both Development and Production,
// because error handling differs between the two.
public class DevelopmentErrorsAndLimitsTests(KestrelFactory factory)
    : ErrorsAndLimitsTests<KestrelFactory>(factory);

public class ProductionErrorsAndLimitsTests(ProductionKestrelFactory factory)
    : ErrorsAndLimitsTests<ProductionKestrelFactory>(factory);

public abstract class ErrorsAndLimitsTests<TFactory> : IClassFixture<TFactory>
    where TFactory : KestrelFactory
{
    private const int Cap = (int)TicketsEndpoints.MaxRequestBodyBytes;
    private readonly HttpClient _client;

    protected ErrorsAndLimitsTests(TFactory factory)
    {
        factory.StartServer();
        _client = factory.CreateClient();
    }

    // ---- Unreadable bodies ----

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{ "title": 123 }""")]
    [InlineData("""{ "title": "x" """)]
    public async Task Unreadable_body_returns_400_problem_details(string json)
    {
        var response = await PostRawAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response);
    }

    [Fact]
    public async Task Non_json_content_type_returns_415_problem_details()
    {
        var response = await _client.PostAsync(
            "/tickets",
            new StringContent("title=t", Encoding.UTF8, "text/plain"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        await AssertProblemDetailsAsync(response);
    }

    // ---- Body size cap ----

    [Fact]
    public async Task Body_one_byte_over_the_cap_returns_413_problem_details()
    {
        var response = await OverCapRequests.SendAsync(_client, HttpMethod.Post, "/tickets", ValidTicketJsonOfSize(Cap + 1));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await AssertProblemDetailsAsync(response);
    }

    [Fact]
    public async Task Body_exactly_at_the_cap_is_accepted()
    {
        var response = await PostRawAsync(ValidTicketJsonOfSize(Cap));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---- A valid ticket never gets 413 because of its encoding ----

    // Exactly 200 and 4000 UTF-16 code units, made of "é" (1 unit) and "😀" (2 units).
    private static readonly string MaxTitle = Repeat("é😀", 66) + "éé";
    private static readonly string MaxDescription = Repeat("é😀", 1333) + "é";

    [Fact]
    public async Task Max_length_multibyte_ticket_is_accepted_as_raw_utf8()
    {
        Assert.Equal(TicketsEndpoints.TitleMaxLength, MaxTitle.Length);
        Assert.Equal(TicketsEndpoints.DescriptionMaxLength, MaxDescription.Length);

        await AssertAcceptedAsync($$"""{ "title": "{{MaxTitle}}", "description": "{{MaxDescription}}" }""");
    }

    [Fact]
    public async Task Max_length_multibyte_ticket_is_accepted_fully_escaped()
    {
        var json = $$"""{ "title": "{{EscapeAll(MaxTitle)}}", "description": "{{EscapeAll(MaxDescription)}}" }""";
        Assert.True(Encoding.UTF8.GetByteCount(json) > 25_000, "Escaped payload should be about 25 KB.");

        await AssertAcceptedAsync(json);
    }

    // ---- Helpers ----

    private async Task AssertAcceptedAsync(string json)
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await PostRawAsync(json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        Assert.Equal(MaxTitle, body.RootElement.GetProperty("title").GetString());
        Assert.Equal(MaxDescription, body.RootElement.GetProperty("description").GetString());
    }

    // A valid ticket padded with an ignored unknown field to exactly `bytes` bytes of UTF-8,
    // so only the size decides the outcome.
    private static string ValidTicketJsonOfSize(int bytes)
    {
        const string prefix = "{\"title\":\"t\",\"pad\":\"";
        const string suffix = "\"}";
        var json = prefix + new string('x', bytes - prefix.Length - suffix.Length) + suffix;
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(json));
        return json;
    }

    private Task<HttpResponseMessage> PostRawAsync(string json) =>
        _client.PostAsync(
            "/tickets",
            new StringContent(json, Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

    private static string Repeat(string s, int count) => string.Concat(Enumerable.Repeat(s, count));

    private static string EscapeAll(string s) => string.Concat(s.Select(c => $"\\u{(int)c:X4}"));

    private static async Task AssertProblemDetailsAsync(HttpResponseMessage response)
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        Assert.Equal((int)response.StatusCode, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("exception", out _), "ProblemDetails leaks exception details.");
    }
}
