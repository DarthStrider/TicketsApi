using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace TicketsApi.Tests.Auth;

// A host with the API's real JWT bearer validation (T-006 criterion 23): no test scheme. No identity provider is
// contacted: the provider's discovery document is replaced by one in memory that holds this factory's issuer and
// a signing key generated for the test, so tokens signed with that key are the only valid ones.
public class JwtFactory : TicketsApiFactory
{
    public const string Issuer = TestAuthority;

    public RsaSecurityKey SigningKey { get; } = new(RSA.Create(2048)) { KeyId = "test-key" };

    protected override bool UseTestAuthentication => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                OpenIdConnectConfiguration discovery = new() { Issuer = Issuer };
                discovery.SigningKeys.Add(SigningKey);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(discovery);
            }));
    }

    // A client that sends `token` as a bearer token (null: no Authorization header).
    public HttpClient CreateClientWithToken(string? token)
    {
        HttpClient client = CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }
}

// Builds test tokens. Valid by default: right issuer and audience, signed with the factory's key, valid now,
// sub "alice". Each parameter breaks one thing, for the criterion 2 cases.
internal static class TestTokens
{
    public static string Create(
        JwtFactory factory,
        string? sub = "alice",
        IReadOnlyDictionary<string, object>? claims = null,
        string issuer = JwtFactory.Issuer,
        string audience = TicketsApiFactory.TestAudience,
        DateTime? notBefore = null,
        DateTime? expires = null,
        SecurityKey? signingKey = null,
        string algorithm = SecurityAlgorithms.RsaSha256)
    {
        DateTime now = DateTime.UtcNow;
        Dictionary<string, object> all = new(claims ?? new Dictionary<string, object>());
        if (sub is not null)
        {
            all["sub"] = sub;
        }

        DateTime validFrom = notBefore ?? now.AddMinutes(-1);
        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = validFrom,
            IssuedAt = validFrom,
            Expires = expires ?? now.AddMinutes(30),
            Claims = all,
            SigningCredentials = new SigningCredentials(signingKey ?? factory.SigningKey, algorithm),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    // The criterion 2 and 4 cases, shared by the Development and Production theories so they can't drift apart.
    public static readonly string[] InvalidScenarios =
    [
        "malformed", "expired", "not yet valid", "other key", "alg none", "no signature", "wrong issuer",
        "wrong audience", "no sub", "empty sub",
    ];

    public static string ForInvalidScenario(JwtFactory factory, string scenario)
    {
        DateTime now = DateTime.UtcNow;
        return scenario switch
        {
            "malformed" => "not-a-jwt",
            // Beyond the default 5-minute clock skew (criterion 9).
            "expired" => Create(factory, notBefore: now.AddHours(-2), expires: now.AddMinutes(-10)),
            "not yet valid" => Create(factory, notBefore: now.AddMinutes(10), expires: now.AddHours(1)),
            "other key" => Create(factory, signingKey: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" }),
            "alg none" => CreateUnsigned(),
            "no signature" => CreateWithoutSignature(factory),
            "wrong issuer" => Create(factory, issuer: "https://evil.test/"),
            "wrong audience" => Create(factory, audience: "another-api"),
            "no sub" => Create(factory, sub: null),
            "empty sub" => Create(factory, sub: ""),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    // A valid RS256 token with its signature removed: the header claims a signature, but there is none.
    public static string CreateWithoutSignature(JwtFactory factory)
    {
        string token = Create(factory);
        return token[..(token.LastIndexOf('.') + 1)];
    }

    // An unsigned token ("alg": "none") with otherwise valid claims.
    public static string CreateUnsigned(string sub = "alice")
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = Base64Url(JsonSerializer.Serialize(new { alg = "none", typ = "JWT" }));
        string payload = Base64Url(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = JwtFactory.Issuer,
            ["aud"] = TicketsApiFactory.TestAudience,
            ["sub"] = sub,
            ["nbf"] = now - 60,
            ["exp"] = now + 1800,
        }));
        return $"{header}.{payload}.";
    }

    private static string Base64Url(string json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));
}
