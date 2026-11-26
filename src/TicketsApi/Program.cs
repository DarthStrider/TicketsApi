using TicketsApi.Auth;
using TicketsApi.Data;
using TicketsApi.Features.Tickets;
using TicketsApi.Localization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddTicketsLocalization(); // includes AddProblemDetails with localized titles

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TicketStoreTestHooks>();
builder.Services.AddTicketsDatabase(); // SQLite through EF Core, from ConnectionStrings:Tickets (T-005)
builder.Services.AddScoped<ITicketStore, EfTicketStore>();
builder.Services.AddTicketsAuth(); // JWT bearer tokens, the agent role and who may read which ticket (T-006)

WebApplication app = builder.Build();

// Fails fast outside Development when the identity provider isn't configured (T-006 criterion 8).
app.ValidateAuthSettings();

// Fails fast without a connection string; migrates in Development, refuses pending migrations elsewhere.
await app.InitializeTicketsDatabaseAsync();

// First, so every response below (including the error handlers' ProblemDetails) is in the request's language.
app.UseTicketsLocalization();

// Every error response is ProblemDetails, without exception details.
// Bad request bodies (malformed JSON, too large) keep their 4xx status instead of becoming 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad
        ? bad.StatusCode
        : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

// After the error handlers, so a 401 or 403 becomes localized ProblemDetails. Every endpoint requires a signed-in
// user unless it opts out with AllowAnonymous (T-006 criteria 1 and 5).
app.UseTicketsAuth();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapTicketsEndpoints();

app.Run();

// Lets the integration tests (WebApplicationFactory<Program>) see this class.
public partial class Program { }
