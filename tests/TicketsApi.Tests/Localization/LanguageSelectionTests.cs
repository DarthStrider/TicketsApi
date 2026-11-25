using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TicketsApi.Features.Tickets;
using TicketsApi.Tests.Tickets;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Localization;

// T-004 criteria 1-7: which language a request gets, and the response headers that say so.
// Each test triggers the same validation error (an empty title) and checks which language its message is in.
public class LanguageSelectionTests(TicketsApiFactory factory)
    : IClassFixture<TicketsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData(null, "en")]        // AC1 no header
    [InlineData("fr", "fr")]        // AC2
    [InlineData("fr-CA", "fr")]     // AC2 region falls back to fr
    [InlineData("fr-FR", "fr")]     // AC2
    [InlineData("FR", "fr")]        // AC2 case-insensitive
    [InlineData("en", "en")]        // AC3
    [InlineData("en-US", "en")]     // AC3
    [InlineData("EN", "en")]        // AC3
    public async Task Language_is_chosen_from_accept_language(string? header, string expected)
    {
        await AssertLanguageAsync(header, expected);
    }

    [Theory]
    [InlineData("fr;q=0.8, en;q=0.9", "en")]
    [InlineData("en;q=0.8, fr;q=0.9", "fr")]
    [InlineData("de, fr;q=0.5", "fr")]
    public async Task Quality_values_pick_the_best_supported_language(string header, string expected)
    {
        await AssertLanguageAsync(header, expected);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("es-MX")]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("fr;q=abc")]
    public async Task Unsupported_or_malformed_header_falls_back_to_english(string header)
    {
        await AssertLanguageAsync(header, "en");
    }

    // AC5: the header never turns a successful request into a 400, 406 or 500. "de, es, it, fr" is English because
    // only the first 3 entries are tried (AC4); here it only has to stay a 200.
    [Theory]
    [InlineData("!!!")]
    [InlineData("fr;q=abc")]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("de, es, it, fr")]
    public async Task Unsupported_or_malformed_header_does_not_change_a_successful_status(string header)
    {
        HttpResponseMessage response = await _client.SendAsync(HttpMethod.Get, "/tickets", header);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["en"], response.Content.Headers.ContentLanguage);
    }

    // AC13/AC14 (security audit): the header changes only the message language (CurrentUICulture). Formatting and
    // comparison (CurrentCulture) stay English. Observed from inside a real request through a store that records
    // both cultures when the endpoint calls it.
    [Theory]
    [InlineData(null, "en")]
    [InlineData("fr", "fr")]
    [InlineData("fr-CA", "fr")]
    [InlineData("en;q=0.8, fr;q=0.9", "fr")]
    public async Task Header_selects_only_the_message_language_not_the_formatting_culture(
        string? header, string expectedUiCulture)
    {
        CultureRecordingStore store = new();
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITicketStore>();
            services.AddSingleton<ITicketStore>(store);
        }));
        HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.SendAsync(HttpMethod.Get, $"/tickets/{Guid.NewGuid()}", header);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("en", store.Culture);
        Assert.Equal(expectedUiCulture, store.UiCulture);
    }

    [Theory]
    [InlineData("/tickets?culture=fr")]
    [InlineData("/tickets?ui-culture=fr")]
    [InlineData("/tickets?culture=fr&ui-culture=fr")]
    public async Task Query_string_does_not_select_the_language(string url)
    {
        HttpResponseMessage response = await _client.SendAsync(HttpMethod.Post, url, json: """{ "title": "" }""");

        await AssertTitleMessageAsync(response, "en");
    }

    [Fact]
    public async Task Culture_cookie_does_not_select_the_language()
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/tickets")
        {
            Content = new StringContent("""{ "title": "" }""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Cookie", ".AspNetCore.Culture=c%3Dfr%7Cuic%3Dfr");

        HttpResponseMessage response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        await AssertTitleMessageAsync(response, "en");
    }

    // AC7: successful and failing responses both say which language was used, and that it depends on the header.
    [Theory]
    [InlineData(null, "en", true)]
    [InlineData("fr", "fr", true)]
    [InlineData(null, "en", false)]
    [InlineData("fr-CA", "fr", false)]
    public async Task Responses_have_content_language_and_vary(string? header, string expected, bool success)
    {
        string json = success ? """{ "title": "ok" }""" : """{ "title": "" }""";

        HttpResponseMessage response = await _client.SendAsync(HttpMethod.Post, "/tickets", header, json);

        Assert.Equal(success ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([expected], response.Content.Headers.ContentLanguage);
        Assert.Contains("Accept-Language", response.Headers.Vary);
    }

    // ---- Helpers ----

    private async Task AssertLanguageAsync(string? header, string expected)
    {
        HttpResponseMessage response = await _client.SendAsync(HttpMethod.Post, "/tickets", header, """{ "title": "" }""");

        await AssertTitleMessageAsync(response, expected);
    }

    // The status code never depends on the header (AC5), and the message is in the expected language.
    private static async Task AssertTitleMessageAsync(HttpResponseMessage response, string language)
    {
        JsonElement body = await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(Texts.Get("TitleRequired", language), body.GetProperty("errors").GetProperty("title")[0].GetString());
        Assert.Equal(Texts.Get("Title400Validation", language), body.GetProperty("title").GetString());
    }

    // Records the cultures in effect when the endpoint looks a ticket up, and finds nothing (404).
    private sealed class CultureRecordingStore : ITicketStore
    {
        public string? Culture { get; private set; }

        public string? UiCulture { get; private set; }

        public Task<Ticket?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            Culture = CultureInfo.CurrentCulture.Name;
            UiCulture = CultureInfo.CurrentUICulture.Name;
            return Task.FromResult<Ticket?>(null);
        }

        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Ticket>> ListAsync(TicketStatus? status, string? createdBy, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Ticket?> TryUpdateStatusAsync(
            Guid id, TicketStatus expected, TicketStatus next, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
