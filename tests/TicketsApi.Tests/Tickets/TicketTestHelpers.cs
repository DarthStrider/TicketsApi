using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TicketsApi.Tests.Tickets;

// A clock the tests can set, so createdAt (and therefore list order) is under test control.
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

internal static class TicketTestHelpers
{
    // Each call builds a new host, so the in-memory store starts empty and other tests' tickets don't leak in.
    public static WebApplicationFactory<Program> WithClock(
        this WebApplicationFactory<Program> factory, TimeProvider clock) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(clock);
        }));

    public static async Task<JsonElement> CreateTicketAsync(
        this HttpClient client, string title, string? description = null)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpResponseMessage response = await client.PostAsJsonAsync("/tickets", new { title, description }, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response, ct);
    }

    public static Task<HttpResponseMessage> PatchStatusAsync(this HttpClient client, Guid id, object body) =>
        client.PatchAsJsonAsync($"/tickets/{id}/status", body, TestContext.Current.CancellationToken);

    // contentType is sent exactly as given (it may carry parameters such as "; charset=utf-8").
    public static Task<HttpResponseMessage> PatchStatusRawAsync(
        this HttpClient client, string id, string json, string contentType = "application/json")
    {
        StringContent content = new(json, System.Text.Encoding.UTF8);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        return client.PatchAsync($"/tickets/{id}/status", content, TestContext.Current.CancellationToken);
    }

    // The allowed path from Open to each status (T-003 criterion 4).
    public static readonly IReadOnlyDictionary<string, string[]> PathFromOpen = new Dictionary<string, string[]>
    {
        ["Open"] = [],
        ["InProgress"] = ["InProgress"],
        ["Resolved"] = ["InProgress", "Resolved"],
        ["InReview"] = ["InProgress", "Resolved", "InReview"],
        ["Closed"] = ["Closed"],
    };

    // Creates a ticket and moves it to `status` through allowed PATCHes. Returns the ticket's id.
    public static async Task<Guid> CreateTicketInStatusAsync(this HttpClient client, string status, string title = "t")
    {
        Guid id = (await client.CreateTicketAsync(title)).GetProperty("id").GetGuid();
        foreach (string step in PathFromOpen[status])
        {
            HttpResponseMessage response = await client.PatchStatusAsync(id, new { status = step });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        return id;
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    // AC17: application/problem+json, status matches the HTTP code, no exception details. No fixed text (T-004).
    public static async Task<JsonElement> AssertProblemDetailsAsync(
        HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ReadJsonAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal((int)expected, body.GetProperty("status").GetInt32());
        Assert.False(body.TryGetProperty("exception", out _), "ProblemDetails leaks exception details.");
        return body;
    }

    public static async Task AssertValidationErrorAsync(HttpResponseMessage response, string field)
    {
        JsonElement body = await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest);
        Assert.True(body.TryGetProperty("errors", out JsonElement errors), "ProblemDetails has no 'errors'.");
        Assert.True(errors.TryGetProperty(field, out _), $"ProblemDetails errors does not name '{field}'.");
    }
}
