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

WebApplication app = builder.Build();

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapTicketsEndpoints();

app.Run();

// Lets the integration tests (WebApplicationFactory<Program>) see this class.
public partial class Program { }
