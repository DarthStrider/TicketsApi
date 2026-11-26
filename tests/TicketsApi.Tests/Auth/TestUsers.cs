using Microsoft.AspNetCore.Mvc.Testing;

namespace TicketsApi.Tests.Auth;

internal static class TestUsers
{
    // A client that calls as `user`, in TestAuthHandler's X-Test-User format.
    public static HttpClient CreateClientAs(this WebApplicationFactory<Program> factory, string user)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.Header, user);
        return client;
    }
}
