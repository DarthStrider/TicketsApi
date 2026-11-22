using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Tickets;

// T-002 criteria 1-3 and 17.
public class GetTicketTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Title = "Imprimante bloquée 🖨️";
    private const string Description = "Café renversé ☕";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Existing_ticket_returns_200_with_same_body_as_post()
    {
        var ct = TestContext.Current.CancellationToken;
        // Multibyte title and a non-null description, so every field in AC1 must round-trip.
        var created = await _client.CreateTicketAsync(Title, Description);
        var id = created.GetProperty("id").GetGuid();

        var response = await _client.GetAsync($"/tickets/{id}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(response, ct);
        Assert.Equal(created.GetRawText(), body.GetRawText());

        // Guards against a vacuous match where both bodies lost the values.
        Assert.Equal(Title, body.GetProperty("title").GetString());
        Assert.Equal(Description, body.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Unknown_guid_returns_404_problem_details()
    {
        var response = await _client.GetAsync(
            $"/tickets/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("00000000-0000-0000-0000-00000000000Z")]
    public async Task Non_guid_id_returns_404_problem_details(string id)
    {
        var response = await _client.GetAsync($"/tickets/{id}", TestContext.Current.CancellationToken);

        await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound);
    }
}
