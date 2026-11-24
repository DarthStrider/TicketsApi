using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TicketsApi.Features.Tickets;
using TicketsApi.Tests.Tickets;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Localization;

// T-004 criteria 7, 9 and 10 on real Kestrel, in Development and Production: the 413 (which only Kestrel produces),
// and the framework's 400 and 404, whose code paths differ between the two environments.
// The 500 in both environments is in LocalizedMessagesTests.Title_500_is_localized.
public class DevelopmentLocalizedLimitsTests(KestrelFactory factory)
    : LocalizedLimitsTests<KestrelFactory>(factory);

public class ProductionLocalizedLimitsTests(ProductionKestrelFactory factory)
    : LocalizedLimitsTests<ProductionKestrelFactory>(factory);

public abstract class LocalizedLimitsTests<TFactory> : IClassFixture<TFactory>
    where TFactory : KestrelFactory
{
    private readonly HttpClient _client;

    protected LocalizedLimitsTests(TFactory factory)
    {
        factory.StartServer();
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("fr", false)]
    [InlineData("en", true)]
    [InlineData("fr", true)]
    public async Task Title_413_is_localized(string language, bool patch)
    {
        string tooLarge = "{\"title\":\"t\",\"pad\":\"" + new string('x', (int)TicketsEndpoints.MaxRequestBodyBytes) + "\"}";
        string url = "/tickets";
        if (patch)
        {
            HttpResponseMessage created = await _client.PostAsJsonAsync("/tickets", new { title = "t" }, TestContext.Current.CancellationToken);
            Guid id = (await ReadJsonAsync(created, TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
            url = $"/tickets/{id}/status";
        }

        HttpResponseMessage response = await OverCapRequests.SendAsync(
            _client, patch ? HttpMethod.Patch : HttpMethod.Post, url, tooLarge, Texts.Header(language));

        JsonElement body = await AssertProblemDetailsAsync(response, HttpStatusCode.RequestEntityTooLarge);
        Assert.Equal(Texts.Get("Title413", language), body.GetProperty("title").GetString());

        // AC7: the PATCH 413 goes through the exception handler, which clears the response headers.
        Assert.Equal([language], response.Content.Headers.ContentLanguage);
        Assert.Contains("Accept-Language", response.Headers.Vary);
    }

    // Malformed and wrong-type JSON on POST are rejected by the framework (AC10). In Development that goes through
    // the exception handler; in Production the endpoint writes the 400 itself.
    [Theory]
    [InlineData("en", "{")]
    [InlineData("fr", "{")]
    [InlineData("en", """{ "title": 5 }""")]
    [InlineData("fr", """{ "title": 5 }""")]
    public async Task Malformed_post_body_title_is_localized(string language, string json)
    {
        HttpResponseMessage response = await _client.SendAsync(HttpMethod.Post, "/tickets", Texts.Header(language), json);

        JsonElement body = await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(Texts.Get("Title400", language), body.GetProperty("title").GetString());
        Assert.Equal([language], response.Content.Headers.ContentLanguage);
        Assert.Contains("Accept-Language", response.Headers.Vary);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task Non_guid_404_title_is_localized(string language)
    {
        HttpResponseMessage response = await _client.SendAsync(HttpMethod.Get, "/tickets/abc", Texts.Header(language));

        JsonElement body = await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound);
        Assert.Equal(Texts.Get("Title404", language), body.GetProperty("title").GetString());
    }
}
