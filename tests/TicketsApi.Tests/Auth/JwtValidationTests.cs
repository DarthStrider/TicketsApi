using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Auth;

// T-006 criteria 1 to 5, 9, 24, 26 and 28, against the API's real JWT bearer validation (criterion 23).
public class JwtValidationTests(JwtFactory factory) : IClassFixture<JwtFactory>
{
    // ---- No token (criteria 1 and 5) ----

    public static TheoryData<string, string> ProtectedRequests => new()
    {
        { "GET", "/tickets" },
        { "POST", "/tickets" },
        { "GET", $"/tickets/{Guid.NewGuid()}" },
        { "PATCH", $"/tickets/{Guid.NewGuid()}/status" },
        { "GET", "/tickets/abc" }, // matches no route
        { "PATCH", "/tickets/abc/status" },
        { "DELETE", "/tickets" }, // verb not mapped
        { "PUT", $"/tickets/{Guid.NewGuid()}/status" },
        { "GET", "/nothing" }, // a path outside /tickets
    };

    [Theory]
    [MemberData(nameof(ProtectedRequests))]
    public async Task No_token_returns_401_problem_details_with_www_authenticate(string method, string url)
    {
        HttpResponseMessage response = await factory.CreateClientWithToken(null)
            .SendAsync(new HttpRequestMessage(new HttpMethod(method), url), TestContext.Current.CancellationToken);

        await AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task Health_is_anonymous()
    {
        HttpResponseMessage response = await factory.CreateClientWithToken(null)
            .GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Openapi_document_is_anonymous_in_development()
    {
        HttpResponseMessage response = await factory.CreateClientWithToken(null)
            .GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- A valid token (the baseline for every 401 case below) ----

    [Fact]
    public async Task Valid_token_is_accepted()
    {
        HttpResponseMessage response = await factory.CreateClientWithToken(TestTokens.Create(factory))
            .GetAsync("/tickets", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Invalid tokens (criteria 2, 4 and 9) ----

    public static TheoryData<string> InvalidTokenCases =>
    [
        "malformed", "expired", "not yet valid", "other key", "alg none", "no signature", "wrong issuer",
        "wrong audience", "no sub", "empty sub",
    ];

    [Theory]
    [MemberData(nameof(InvalidTokenCases))]
    public async Task Invalid_token_returns_401(string scenario)
    {
        DateTime now = DateTime.UtcNow;
        string token = scenario switch
        {
            "malformed" => "not-a-jwt",
            // Beyond the default 5-minute clock skew (criterion 9).
            "expired" => TestTokens.Create(factory, notBefore: now.AddHours(-2), expires: now.AddMinutes(-10)),
            "not yet valid" => TestTokens.Create(factory, notBefore: now.AddMinutes(10), expires: now.AddHours(1)),
            "other key" => TestTokens.Create(factory, signingKey: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" }),
            "alg none" => TestTokens.CreateUnsigned(),
            "no signature" => TestTokens.CreateWithoutSignature(factory),
            "wrong issuer" => TestTokens.Create(factory, issuer: "https://evil.test/"),
            "wrong audience" => TestTokens.Create(factory, audience: "another-api"),
            "no sub" => TestTokens.Create(factory, sub: null),
            "empty sub" => TestTokens.Create(factory, sub: ""),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        HttpResponseMessage response = await factory.CreateClientWithToken(token)
            .GetAsync("/tickets", TestContext.Current.CancellationToken);

        await AssertUnauthorizedAsync(response);
    }

    // The framework's default clock skew is 5 minutes: a token that expired 2 minutes ago is still accepted.
    [Fact]
    public async Task Clock_skew_is_the_framework_default()
    {
        DateTime now = DateTime.UtcNow;
        string token = TestTokens.Create(factory, notBefore: now.AddHours(-1), expires: now.AddMinutes(-2));

        HttpResponseMessage response = await factory.CreateClientWithToken(token)
            .GetAsync("/tickets", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Another_scheme_returns_401()
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "YWxpY2U6cGFzcw==");

        HttpResponseMessage response = await client.GetAsync("/tickets", TestContext.Current.CancellationToken);

        await AssertUnauthorizedAsync(response);
    }

    // ---- Only the Authorization header (criterion 3) ----

    [Fact]
    public async Task Token_in_query_string_is_ignored()
    {
        string token = TestTokens.Create(factory);

        HttpResponseMessage response = await factory.CreateClientWithToken(null)
            .GetAsync($"/tickets?access_token={token}", TestContext.Current.CancellationToken);

        await AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task Token_in_cookie_is_ignored()
    {
        string token = TestTokens.Create(factory);
        HttpRequestMessage request = new(HttpMethod.Get, "/tickets");
        request.Headers.Add("Cookie", $"access_token={token}; Authorization=Bearer%20{token}");

        HttpResponseMessage response = await factory.CreateClientWithToken(null)
            .SendAsync(request, TestContext.Current.CancellationToken);

        await AssertUnauthorizedAsync(response);
    }

    // ---- No token details in errors (criteria 26 and 28) ----

    [Fact]
    public async Task Rejected_token_details_are_not_disclosed()
    {
        DateTime now = DateTime.UtcNow;
        string token = TestTokens.Create(factory, notBefore: now.AddHours(-2), expires: now.AddMinutes(-10));

        HttpResponseMessage response = await factory.CreateClientWithToken(token)
            .GetAsync("/tickets", TestContext.Current.CancellationToken);

        await AssertUnauthorizedAsync(response);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("expired", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alice", body);
        Assert.DoesNotContain(token, body);
    }

    // ---- The test scheme is not in a real host (criterion 24) ----

    [Fact]
    public async Task Only_the_bearer_scheme_is_registered()
    {
        IAuthenticationSchemeProvider schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        IEnumerable<string> names = (await schemes.GetAllSchemesAsync()).Select(s => s.Name);

        Assert.Equal(["Bearer"], names);
    }

    [Fact]
    public async Task Test_user_header_is_ignored_by_a_real_host()
    {
        HttpRequestMessage request = new(HttpMethod.Get, "/tickets");
        request.Headers.Add(TestAuthHandler.Header, TestAuthHandler.DefaultUser);

        HttpResponseMessage response = await factory.CreateClientWithToken(null)
            .SendAsync(request, TestContext.Current.CancellationToken);

        await AssertUnauthorizedAsync(response);
    }

    // Criterion 26 (ProblemDetails, no token details) and 28 (WWW-Authenticate: Bearer, never error_description).
    private static async Task AssertUnauthorizedAsync(HttpResponseMessage response)
    {
        JsonElement body = await AssertProblemDetailsAsync(response, HttpStatusCode.Unauthorized);
        Assert.False(body.TryGetProperty("errors", out _));

        string challenge = Assert.Single(response.Headers.WwwAuthenticate).ToString();
        Assert.StartsWith("Bearer", challenge);
        Assert.DoesNotContain("error_description", challenge);
    }
}
