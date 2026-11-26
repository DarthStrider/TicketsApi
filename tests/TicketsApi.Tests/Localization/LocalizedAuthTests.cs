using System.Net;
using System.Text.Json;
using TicketsApi.Tests.Auth;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Localization;

// T-006 criterion 27: the 401 and 403 titles are localized like every other error (T-004 criteria 7 to 10, 17),
// on the API's real JWT bearer validation (401) and on a real role check (403).
public class LocalizedAuthTests(JwtFactory factory) : IClassFixture<JwtFactory>
{
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task Title_401_is_localized(string language)
    {
        HttpResponseMessage response = await factory.CreateClientWithToken(null)
            .SendAsync(HttpMethod.Get, "/tickets", Texts.Header(language));

        await AssertLocalizedTitleAsync(response, HttpStatusCode.Unauthorized, "Title401", language);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task Title_403_is_localized(string language)
    {
        HttpResponseMessage response = await factory.CreateClientWithToken(TestTokens.Create(factory))
            .SendAsync(HttpMethod.Patch, $"/tickets/{Guid.NewGuid()}/status", Texts.Header(language), """{ "status": "Closed" }""");

        await AssertLocalizedTitleAsync(response, HttpStatusCode.Forbidden, "Title403", language);
    }

    private static async Task AssertLocalizedTitleAsync(
        HttpResponseMessage response, HttpStatusCode status, string key, string language)
    {
        JsonElement body = await AssertProblemDetailsAsync(response, status);
        Assert.Equal(Texts.Get(key, language), body.GetProperty("title").GetString());
        Assert.Equal([language], response.Content.Headers.ContentLanguage);
        Assert.Contains("Accept-Language", response.Headers.Vary);
    }
}
