using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Auth;

// T-006 criteria 6 and 9 outside Development, where configuration must not be able to weaken token validation:
// metadata only over HTTPS, signing keys only from the provider's discovery document, asymmetric algorithms only.
// (In Development, `dotnet user-jwts` relies on symmetric keys from user-secrets, so these rules don't apply there.)
public class ProductionJwtTests(JwtFactory factory) : IClassFixture<JwtFactory>
{
    // ---- The criterion 2 cases on a Production host, whose settings are only Authority and Audience ----

    [Fact]
    public async Task Valid_token_is_accepted_in_production()
    {
        using WebApplicationFactory<Program> app = Production();

        HttpResponseMessage response = await SendAsync(app, TestTokens.Create(factory));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static TheoryData<string> InvalidTokenCases =>
    [
        "expired", "not yet valid", "other key", "alg none", "wrong issuer", "wrong audience", "no sub",
    ];

    [Theory]
    [MemberData(nameof(InvalidTokenCases))]
    public async Task Invalid_token_returns_401_in_production(string scenario)
    {
        DateTime now = DateTime.UtcNow;
        string token = scenario switch
        {
            "expired" => TestTokens.Create(factory, notBefore: now.AddHours(-2), expires: now.AddMinutes(-10)),
            "not yet valid" => TestTokens.Create(factory, notBefore: now.AddMinutes(10), expires: now.AddHours(1)),
            "other key" => TestTokens.Create(factory, signingKey: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" }),
            "alg none" => TestTokens.CreateUnsigned(),
            "wrong issuer" => TestTokens.Create(factory, issuer: "https://evil.test/"),
            "wrong audience" => TestTokens.Create(factory, audience: "another-api"),
            "no sub" => TestTokens.Create(factory, sub: null),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        using WebApplicationFactory<Program> app = Production();

        HttpResponseMessage response = await SendAsync(app, token);

        await AssertProblemDetailsAsync(response, HttpStatusCode.Unauthorized);
    }

    // ---- Configuration can't weaken validation outside Development ----

    // Criterion 9: metadata only over HTTPS, even when configuration tries to turn that off.
    [Fact]
    public void Http_authority_fails_startup_in_production_even_if_configuration_allows_it()
    {
        using WebApplicationFactory<Program> app = Production(new()
        {
            ["Authentication:Schemes:Bearer:Authority"] = "http://login.tickets.test/",
            ["Authentication:Schemes:Bearer:RequireHttpsMetadata"] = "false",
        });

        Exception error = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("HTTPS", AllMessages(error), StringComparison.OrdinalIgnoreCase);
    }

    // Criterion 6: keys come only from the provider's discovery document. A symmetric key in configuration (the
    // `dotnet user-jwts` format) must not let anyone who knows it mint tokens in Production.
    [Fact]
    public async Task Signing_key_from_configuration_is_ignored_in_production()
    {
        (SymmetricSecurityKey key, Dictionary<string, string?> settings) = ConfiguredSymmetricKey();
        using WebApplicationFactory<Program> app = Production(settings);

        HttpResponseMessage response = await SendAsync(app, SymmetricToken(key));

        await AssertProblemDetailsAsync(response, HttpStatusCode.Unauthorized);
    }

    // Criterion 6, "asymmetric": even a symmetric key that the provider's own discovery document publishes is refused,
    // so only the algorithm allow-list stops an HS256 token here.
    [Fact]
    public async Task Symmetric_key_from_discovery_is_rejected_in_production()
    {
        SymmetricSecurityKey hmac = new(RandomNumberGenerator.GetBytes(32)) { KeyId = "hmac" };
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    OpenIdConnectConfiguration discovery = new() { Issuer = JwtFactory.Issuer };
                    discovery.SigningKeys.Add(factory.SigningKey);
                    discovery.SigningKeys.Add(hmac);
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(discovery);
                })));

        HttpResponseMessage rsa = await SendAsync(app, TestTokens.Create(factory));
        HttpResponseMessage hs256 = await SendAsync(app, SymmetricToken(hmac));

        Assert.Equal(HttpStatusCode.OK, rsa.StatusCode); // the host itself works
        await AssertProblemDetailsAsync(hs256, HttpStatusCode.Unauthorized);
    }

    // The same configured key still works in Development, where `dotnet user-jwts` needs it (criterion 7).
    [Fact]
    public async Task Signing_key_from_configuration_works_in_development()
    {
        (SymmetricSecurityKey key, Dictionary<string, string?> settings) = ConfiguredSymmetricKey();
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings)));

        HttpResponseMessage response = await SendAsync(app, SymmetricToken(key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Helpers ----

    private WebApplicationFactory<Program> Production(Dictionary<string, string?>? settings = null) =>
        factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings ?? [])));

    private static Task<HttpResponseMessage> SendAsync(WebApplicationFactory<Program> app, string token)
    {
        HttpClient client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.GetAsync("/tickets", TestContext.Current.CancellationToken);
    }

    // A key in the format `dotnet user-jwts` writes to user-secrets, for the factory's issuer. The framework only
    // uses a configured key whose issuer is also a configured ValidIssuer, so both are set, as an attacker would.
    private static (SymmetricSecurityKey Key, Dictionary<string, string?> Settings) ConfiguredSymmetricKey()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        Dictionary<string, string?> settings = new()
        {
            ["Authentication:Schemes:Bearer:ValidIssuer"] = JwtFactory.Issuer,
            ["Authentication:Schemes:Bearer:SigningKeys:0:Issuer"] = JwtFactory.Issuer,
            ["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = Convert.ToBase64String(bytes),
            ["Authentication:Schemes:Bearer:SigningKeys:0:Length"] = "32",
        };
        return (new SymmetricSecurityKey(bytes), settings);
    }

    private string SymmetricToken(SymmetricSecurityKey key) =>
        TestTokens.Create(factory, signingKey: key, algorithm: SecurityAlgorithms.HmacSha256);

    private static string AllMessages(Exception error)
    {
        List<string> messages = [];
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }
}
