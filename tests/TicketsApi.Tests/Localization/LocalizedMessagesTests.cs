using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TicketsApi.Features.Tickets;
using TicketsApi.Tests.Tickets;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Localization;

// T-004 criteria 8-14, 17 and 18: the exact EN/FR text of every message and title, and what must not change.
// "en" sends no header (the default); "fr" sends Accept-Language: fr.
public class LocalizedMessagesTests(TicketsApiFactory factory)
    : IClassFixture<TicketsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    // ---- Field messages in errors (AC8, AC12, AC13, AC17) ----

    public static TheoryData<string, string> FieldCases => new()
    {
        { "TitleRequired", "en" }, { "TitleRequired", "fr" },
        { "TitleTooLong", "en" }, { "TitleTooLong", "fr" },
        { "DescriptionTooLong", "en" }, { "DescriptionTooLong", "fr" },
        { "StatusRequired", "en" }, { "StatusRequired", "fr" },
        { "StatusMustBeOneOf", "en" }, { "StatusMustBeOneOf", "fr" },
        { "StatusRepeated", "en" }, { "StatusRepeated", "fr" },
        { "BodyInvalid", "en" }, { "BodyInvalid", "fr" },
    };

    [Theory]
    [MemberData(nameof(FieldCases))]
    public async Task Field_message_text_is_exact(string key, string language)
    {
        var id = await _client.CreateTicketInStatusAsync("Open");
        var header = Texts.Header(language);

        var (response, field) = key switch
        {
            "TitleRequired" => (await PostAsync(header, """{ "title": "" }"""), "title"),
            "TitleTooLong" => (await PostAsync(header, $$"""{ "title": "{{new string('a', 201)}}" }"""), "title"),
            "DescriptionTooLong" => (
                await PostAsync(header, $$"""{ "title": "t", "description": "{{new string('d', 4001)}}" }"""),
                "description"),
            "StatusRequired" => (await PatchAsync(id, header, "{}"), "status"),
            "StatusMustBeOneOf" => (await PatchAsync(id, header, """{ "status": "Banana" }"""), "status"),
            "StatusRepeated" => (await PatchAsync(id, header, """{ "status": "Open", "status": "Open" }"""), "status"),
            "BodyInvalid" => (await PatchAsync(id, header, "[]"), "body"),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };

        await AssertFieldMessageAsync(response, field, Texts.Get(key, language), language);
    }

    // The same messages on the other endpoints and error paths that use them (AC8).
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task List_filter_message_is_localized(string language)
    {
        var response = await _client.SendAsync(HttpMethod.Get, "/tickets?status=Banana", Texts.Header(language));

        await AssertFieldMessageAsync(response, "status", Texts.Get("StatusMustBeOneOf", language), language);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task Unparsable_patch_body_message_is_localized(string language)
    {
        var id = await _client.CreateTicketInStatusAsync("Open");

        var response = await PatchAsync(id, Texts.Header(language), "{");

        await AssertFieldMessageAsync(response, "body", Texts.Get("BodyInvalid", language), language);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task Null_status_message_is_localized(string language)
    {
        var id = await _client.CreateTicketInStatusAsync("Open");

        var response = await PatchAsync(id, Texts.Header(language), """{ "status": null }""");

        await AssertFieldMessageAsync(response, "status", Texts.Get("StatusRequired", language), language);
    }

    // ---- ProblemDetails titles (AC9, AC10, AC17) ----

    public static TheoryData<string, string> TitleCases => new()
    {
        { "Title400", "en" }, { "Title400", "fr" },
        { "Title400WrongType", "en" }, { "Title400WrongType", "fr" },
        { "Title404Unknown", "en" }, { "Title404Unknown", "fr" },
        { "Title404NonGuid", "en" }, { "Title404NonGuid", "fr" },
        { "Title405", "en" }, { "Title405", "fr" },
        { "Title409", "en" }, { "Title409", "fr" },
        { "Title415Post", "en" }, { "Title415Post", "fr" },
        { "Title415Patch", "en" }, { "Title415Patch", "fr" },
    };

    [Theory]
    [MemberData(nameof(TitleCases))]
    public async Task Problem_title_text_is_exact(string scenario, string language)
    {
        var id = await _client.CreateTicketInStatusAsync("Open");
        var header = Texts.Header(language);

        var (response, status, key) = scenario switch
        {
            // Malformed JSON on POST is rejected by the framework (no errors), not by our validation.
            "Title400" => (await PostAsync(header, "{"), HttpStatusCode.BadRequest, "Title400"),
            // Wrong-type JSON on POST is also rejected by the framework (AC10).
            "Title400WrongType" => (
                await PostAsync(header, """{ "title": 5 }"""), HttpStatusCode.BadRequest, "Title400"),
            "Title404Unknown" => (
                await _client.SendAsync(HttpMethod.Get, $"/tickets/{Guid.NewGuid()}", header),
                HttpStatusCode.NotFound,
                "Title404"),
            "Title404NonGuid" => (
                await _client.SendAsync(HttpMethod.Get, "/tickets/abc", header), HttpStatusCode.NotFound, "Title404"),
            "Title405" => (
                await _client.SendAsync(HttpMethod.Put, $"/tickets/{id}/status", header, """{ "status": "Closed" }"""),
                HttpStatusCode.MethodNotAllowed,
                "Title405"),
            "Title409" => (
                await PatchAsync(id, header, """{ "status": "Resolved" }"""), HttpStatusCode.Conflict, "Title409"),
            "Title415Post" => (
                await _client.SendAsync(HttpMethod.Post, "/tickets", header, "title=t", "text/plain"),
                HttpStatusCode.UnsupportedMediaType,
                "Title415"),
            "Title415Patch" => (
                await _client.SendAsync(
                    HttpMethod.Patch, $"/tickets/{id}/status", header, """{ "status": "Closed" }""", "text/plain"),
                HttpStatusCode.UnsupportedMediaType,
                "Title415"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var body = await AssertProblemDetailsAsync(response, status);
        Assert.Equal(Texts.Get(key, language), body.GetProperty("title").GetString());

        // AC7 on every error path. In Development the framework's 400s go through the exception handler,
        // which clears the response headers before writing its ProblemDetails.
        Assert.Equal([language], response.Content.Headers.ContentLanguage);
        Assert.Contains("Accept-Language", response.Headers.Vary);
    }

    // The 500 goes through the exception handler, which clears the response headers, so AC7 is checked here too.
    // Runs in Development and Production (AC9, AC10, AC18).
    [Theory]
    [InlineData("en", "Development")]
    [InlineData("fr", "Development")]
    [InlineData("en", "Production")]
    [InlineData("fr", "Production")]
    public async Task Title_500_is_localized(string language, string environment)
    {
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITicketStore>();
                services.AddSingleton<ITicketStore, ThrowingTicketStore>();
            });
        });
        var client = app.CreateClient();

        var response = await client.SendAsync(HttpMethod.Get, $"/tickets/{Guid.NewGuid()}", Texts.Header(language));

        var body = await AssertProblemDetailsAsync(response, HttpStatusCode.InternalServerError);
        Assert.Equal(Texts.Get("Title500", language), body.GetProperty("title").GetString());
        Assert.Equal([language], response.Content.Headers.ContentLanguage);
        Assert.Contains("Accept-Language", response.Headers.Vary);
        Assert.DoesNotContain("store is broken", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // ---- What clients parse does not change (AC11, AC12) ----

    [Fact]
    public async Task Only_text_changes_between_languages_for_validation_errors()
    {
        var json = $$"""{ "title": "", "description": "{{new string('d', 4001)}}" }""";

        var enResponse = await PostAsync(null, json);
        var frResponse = await PostAsync("fr", json);
        var en = await ReadJsonAsync(enResponse, TestContext.Current.CancellationToken);
        var fr = await ReadJsonAsync(frResponse, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, enResponse.StatusCode);
        Assert.Equal(enResponse.StatusCode, frResponse.StatusCode);
        Assert.Equal(en.GetProperty("status").GetInt32(), fr.GetProperty("status").GetInt32());
        Assert.Equal(en.GetProperty("type").GetString(), fr.GetProperty("type").GetString());
        Assert.Equal(Keys(en.GetProperty("errors")), Keys(fr.GetProperty("errors")));
        Assert.Equal(["description", "title"], Keys(fr.GetProperty("errors")));
        Assert.NotEqual(en.GetProperty("title").GetString(), fr.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Only_text_changes_between_languages_for_conflicts()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await _client.CreateTicketInStatusAsync("Open");

        var en = await ReadJsonAsync(await PatchAsync(id, null, """{ "status": "Resolved" }"""), ct);
        var fr = await ReadJsonAsync(await PatchAsync(id, "fr", """{ "status": "resolved" }"""), ct);

        Assert.Equal(en.GetProperty("status").GetInt32(), fr.GetProperty("status").GetInt32());
        Assert.Equal(en.GetProperty("type").GetString(), fr.GetProperty("type").GetString());
        Assert.Equal("Open", fr.GetProperty("currentStatus").GetString());
        Assert.Equal("Resolved", fr.GetProperty("requestedStatus").GetString());
        Assert.Equal(en.GetProperty("currentStatus").GetString(), fr.GetProperty("currentStatus").GetString());
        Assert.Equal(en.GetProperty("requestedStatus").GetString(), fr.GetProperty("requestedStatus").GetString());
    }

    // ---- Successful responses do not depend on the language (AC14) ----

    [Fact]
    public async Task Successful_responses_are_identical_in_both_languages()
    {
        var ct = TestContext.Current.CancellationToken;
        using var app = factory.WithClock(new TestClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)));
        var client = app.CreateClient();

        var created = await client.SendAsync(HttpMethod.Post, "/tickets", "fr", """{ "title": "Café", "description": "é" }""");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await ReadJsonAsync(created, ct);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal("Open", body.GetProperty("status").GetString());
        Assert.Equal("2026-01-01T12:00:00+00:00", body.GetProperty("createdAt").GetString());

        foreach (var url in new[] { $"/tickets/{id}", "/tickets", "/tickets?status=Open" })
        {
            var en = await (await client.SendAsync(HttpMethod.Get, url)).Content.ReadAsStringAsync(ct);
            var fr = await (await client.SendAsync(HttpMethod.Get, url, "fr")).Content.ReadAsStringAsync(ct);
            Assert.Equal(en, fr);
        }
    }

    [Theory]
    [InlineData("inprogress")]
    [InlineData("INPROGRESS")]
    [InlineData("InProgress")]
    public async Task French_header_does_not_change_status_matching(string requested)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await _client.CreateTicketInStatusAsync("Open");

        var patch = await PatchAsync(id, "fr", $$"""{ "status": "{{requested}}" }""");
        var list = await _client.SendAsync(HttpMethod.Get, $"/tickets?status={requested}", "fr");

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal("InProgress", (await ReadJsonAsync(patch, ct)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    // ---- Helpers ----

    private Task<HttpResponseMessage> PostAsync(string? header, string json) =>
        _client.SendAsync(HttpMethod.Post, "/tickets", header, json);

    private Task<HttpResponseMessage> PatchAsync(Guid id, string? header, string json) =>
        _client.SendAsync(HttpMethod.Patch, $"/tickets/{id}/status", header, json);

    private static async Task AssertFieldMessageAsync(
        HttpResponseMessage response, string field, string expected, string language)
    {
        var body = await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest);
        var messages = body.GetProperty("errors").GetProperty(field).EnumerateArray().Select(m => m.GetString());
        Assert.Equal([expected], messages);
        Assert.Equal(Texts.Get("Title400Validation", language), body.GetProperty("title").GetString());
    }

    private static List<string> Keys(JsonElement errors) =>
        errors.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();

    // A store whose every call throws, to produce a 500 (T-004 criterion 9).
    private sealed class ThrowingTicketStore : ITicketStore
    {
        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken) => throw Broken();

        public Task<Ticket?> GetAsync(Guid id, CancellationToken cancellationToken) => throw Broken();

        public Task<IReadOnlyList<Ticket>> ListAsync(TicketStatus? status, CancellationToken cancellationToken) =>
            throw Broken();

        public Task<Ticket?> TryUpdateStatusAsync(
            Guid id, TicketStatus expected, TicketStatus next, CancellationToken cancellationToken) => throw Broken();

        private static InvalidOperationException Broken() => new("The store is broken (test).");
    }
}
