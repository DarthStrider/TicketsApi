using TicketsApi.Features.Tickets;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TicketStoreTestHooks>();
builder.Services.AddSingleton<ITicketStore, InMemoryTicketStore>();

var app = builder.Build();

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
