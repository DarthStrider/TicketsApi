var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Starting point only. Tickets T-001 to T-006 in docs/tickets are built with Claude Code.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Lets the integration tests (WebApplicationFactory<Program>) see this class.
public partial class Program { }
