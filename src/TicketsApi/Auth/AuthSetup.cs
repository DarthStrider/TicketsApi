using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TicketsApi.Features.Tickets;

namespace TicketsApi.Auth;

// Authentication and authorization for the whole API (T-006).
// Tokens are JWT bearer tokens from an OpenID Connect provider, configured under Authentication:Schemes:Bearer
// (criterion 6). In Development they come from `dotnet user-jwts` (criterion 7).
internal static class AuthSetup
{
    public const string AgentPolicy = "Agent";

    // Settings read from configuration, never from code (criteria 6 and 10).
    public const string AuthorityKey = "Authentication:Schemes:Bearer:Authority";
    public const string AudienceKey = "Authentication:Schemes:Bearer:Audience";
    public const string RoleClaimKey = "Authentication:RoleClaim";
    public const string DefaultRoleClaim = "role"; // what `dotnet user-jwts` emits

    // Outside Development the provider signs with an asymmetric key (criterion 6), so these are the only algorithms
    // accepted. A symmetric (HS*) algorithm would let anyone who holds the key mint tokens.
    private static readonly string[] AsymmetricAlgorithms =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512,
    ];

    public static string RoleClaim(IConfiguration configuration) =>
        configuration[RoleClaimKey] is { Length: > 0 } configured ? configured : DefaultRoleClaim;

    public static IServiceCollection AddTicketsAuth(this IServiceCollection services)
    {
        // AddJwtBearer() binds Authentication:Schemes:Bearer: Authority, ValidIssuer(s), ValidAudience(s),
        // RequireHttpsMetadata and the `dotnet user-jwts` signing keys, among others. It does not read Audience.
        // The Configure step below runs after that binding and decides what is allowed.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured from IConfiguration when the options are first used, not when the app is built,
        // so environment variables and test hosts' settings always apply.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration, IHostEnvironment>((options, configuration, environment) =>
            {
                if (configuration[AuthorityKey] is { Length: > 0 } authority)
                {
                    options.Authority = authority;
                }

                if (configuration[AudienceKey] is { Length: > 0 } audience)
                {
                    options.Audience = audience;
                }

                // Keep the token's own claim names: `sub` stays `sub`, and the role claim keeps its configured name,
                // including a namespaced one such as Auth0's (criteria 4 and 10). TicketAccess reads the role claim.
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = ClaimsPrincipalExtensions.SubjectClaim;

                // Issuer, audience, lifetime and signature are always validated, with the default 5-minute clock
                // skew (criterion 9). With JwtBearer 10.0.15 these are already the effective values (QA checked that
                // removing them changes nothing); they are stated here so no configuration or later version can
                // quietly turn them off.
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateLifetime = true;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                options.TokenValidationParameters.RequireSignedTokens = true;

                // Outside Development, configuration must not be able to weaken this (criteria 6 and 9):
                // metadata only over HTTPS, signing keys only from the provider's discovery document (never a
                // configured key), and asymmetric algorithms only. Development keeps the `dotnet user-jwts` keys.
                if (!environment.IsDevelopment())
                {
                    options.RequireHttpsMetadata = true;
                    options.TokenValidationParameters.IssuerSigningKey = null;
                    options.TokenValidationParameters.IssuerSigningKeys = null;
                    options.TokenValidationParameters.ValidAlgorithms = AsymmetricAlgorithms;
                }

                // A 401 never explains why a token was rejected (criteria 26 and 28): no error_description.
                options.IncludeErrorDetails = false;

                // Every user is identified by `sub` (criterion 4). A token without one is rejected with a 401.
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (string.IsNullOrEmpty(context.Principal?.FindFirst(ClaimsPrincipalExtensions.SubjectClaim)?.Value))
                        {
                            context.Fail("The token has no subject.");
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorizationBuilder()
            // Every endpoint requires a signed-in user unless it opts out with AllowAnonymous, including requests that
            // match no route, so an anonymous caller can't learn which routes exist (criterion 1).
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            // Changing a status needs the agent role (criterion 20). The rule itself lives in ITicketAccess.
            .AddPolicy(AgentPolicy, policy => policy.RequireAuthenticatedUser().AddRequirements(new AgentRequirement()));

        services.AddSingleton<IAuthorizationHandler, AgentAuthorizationHandler>();
        services.AddSingleton<ITicketAccess, TicketAccess>();
        return services;
    }

    // Fails fast outside Development when the identity provider isn't configured, so the API never starts with
    // authentication that can't work (criterion 8). Development uses `dotnet user-jwts`, which needs neither setting.
    public static void ValidateAuthSettings(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            return;
        }

        foreach (string key in new[] { AuthorityKey, AudienceKey })
        {
            if (string.IsNullOrWhiteSpace(app.Configuration[key]))
            {
                throw new InvalidOperationException(
                    $"The identity provider is not configured: set {key} " +
                    $"(for example with the environment variable {key.Replace(":", "__")}).");
            }
        }

        // Building the options now surfaces any other invalid bearer setting at startup, not on the first request,
        // for example an http:// authority, which RequireHttpsMetadata rejects (criterion 9).
        _ = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
    }

    // After the exception handler and the status-code pages, so a 401 or 403 becomes localized ProblemDetails
    // with Content-Language (criteria 26 and 27).
    public static WebApplication UseTicketsAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
