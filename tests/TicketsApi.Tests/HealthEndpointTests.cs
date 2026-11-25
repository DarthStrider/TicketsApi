using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TicketsApi.Tests;

public class HealthEndpointTests(TicketsApiFactory factory)
    : IClassFixture<TicketsApiFactory>
{
    [Fact]
    public async Task Health_returns_200()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
