using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Auth;

// T-006 criteria 5, 6 and 8: the identity provider settings, and failing fast when they are missing.
public class AuthConfigurationTests(JwtFactory factory) : IClassFixture<JwtFactory>
{
    public static TheoryData<string, string> MissingSettings => new()
    {
        { "Production", "Authentication:Schemes:Bearer:Authority" },
        { "Production", "Authentication:Schemes:Bearer:Audience" },
        { "Staging", "Authentication:Schemes:Bearer:Authority" },
        { "Staging", "Authentication:Schemes:Bearer:Audience" },
    };

    [Theory]
    [MemberData(nameof(MissingSettings))]
    public void Startup_fails_outside_development_when_a_setting_is_missing(string environment, string key)
    {
        using WebApplicationFactory<Program> app = WithSetting(environment, key, "");

        Exception error = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains(key, AllMessages(error));
    }

    [Theory]
    [InlineData("Authentication:Schemes:Bearer:Authority")]
    [InlineData("Authentication:Schemes:Bearer:Audience")]
    public async Task Development_starts_without_the_settings(string key)
    {
        using WebApplicationFactory<Program> app = WithSetting("Development", key, "");

        HttpResponseMessage response = await app.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Production_starts_with_the_settings_and_protects_the_openapi_path()
    {
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        HttpClient client = app.CreateClient();

        HttpResponseMessage health = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        HttpResponseMessage openapi = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        await AssertProblemDetailsAsync(openapi, HttpStatusCode.Unauthorized); // not mapped, so like any path (criterion 5)
    }

    // Criterion 6: nothing in appsettings.json for the identity provider.
    [Fact]
    public void Appsettings_json_has_no_identity_provider_settings()
    {
        string path = Path.Combine(RepositoryRoot(), "src", "TicketsApi", "appsettings.json");

        string json = File.ReadAllText(path);

        Assert.DoesNotContain("Authentication", json);
    }

    private WebApplicationFactory<Program> WithSetting(string environment, string key, string value) =>
        factory.WithWebHostBuilder(builder => builder
            .UseEnvironment(environment)
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { [key] = value })));

    private static string AllMessages(Exception error)
    {
        List<string> messages = [];
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "TicketsApi.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("TicketsApi.slnx not found above the test output.");
    }
}
