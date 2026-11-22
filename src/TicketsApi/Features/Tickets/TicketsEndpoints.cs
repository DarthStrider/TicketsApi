using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace TicketsApi.Features.Tickets;

public static class TicketsEndpoints
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;

    // The largest valid body, fully \u-escaped, is about 25 KB, so 32 KB never rejects a valid ticket.
    public const long MaxRequestBodyBytes = 32 * 1024;

    public static IEndpointRouteBuilder MapTicketsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tickets");

        group.MapPost("", CreateTicketAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes));

        // The :guid constraint makes a non-GUID id fall through to a 404 (T-002 criterion 3).
        group.MapGet("{id:guid}", GetTicketAsync);
        group.MapGet("", ListTicketsAsync);

        return app;
    }

    private static async Task<Results<Ok<TicketResponse>, NotFound>> GetTicketAsync(
        Guid id,
        ITicketStore store,
        CancellationToken cancellationToken)
    {
        var ticket = await store.GetAsync(id, cancellationToken);

        // The empty 404 becomes ProblemDetails through AddProblemDetails + UseStatusCodePages in Program.cs.
        return ticket is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ToResponse(ticket));
    }

    // Bound as an array so a repeated ?status= can be detected and rejected (T-002 criterion 15).
    private static async Task<Results<Ok<TicketListResponse>, ValidationProblem>> ListTicketsAsync(
        [FromQuery] string[]? status,
        ITicketStore store,
        CancellationToken cancellationToken)
    {
        TicketStatus? filter = null;
        if (status is { Length: > 0 })
        {
            if (status.Length > 1 || !TryParseStatus(status[0], out var parsed))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"Status must be one of: {string.Join(", ", Enum.GetNames<TicketStatus>())}."],
                });
            }

            filter = parsed;
        }

        var tickets = await store.ListAsync(filter, cancellationToken);

        return TypedResults.Ok(new TicketListResponse([.. tickets.Select(ToResponse)]));
    }

    // Matches only defined status names, ignoring case. Enum.TryParse is not used on its own because it
    // accepts numeric strings, even undefined ones like "99", and also trims spaces and accepts "A,B" lists.
    private static bool TryParseStatus(string value, out TicketStatus status)
    {
        foreach (var candidate in Enum.GetValues<TicketStatus>())
        {
            if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }

    private static TicketResponse ToResponse(Ticket ticket) =>
        new(ticket.Id, ticket.Title, ticket.Description, ticket.Status, ticket.CreatedAt);

    private static async Task<Results<Created<TicketResponse>, ValidationProblem>> CreateTicketAsync(
        CreateTicketRequest request,
        ITicketStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        if (!IsValid(title, request.Description, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var ticket = new Ticket
        {
            Id = Guid.CreateVersion7(),
            Title = title,
            Description = request.Description,
            Status = TicketStatus.Open,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        await store.AddAsync(ticket, cancellationToken);

        return TypedResults.Created($"/tickets/{ticket.Id}", ToResponse(ticket));
    }

    // Lengths are UTF-16 code units (string.Length), per the T-001 acceptance criteria.
    // A null or empty title is always an error, so a valid result guarantees a non-null title.
    private static bool IsValid(
        [NotNullWhen(true)] string? title,
        string? description,
        out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>();

        if (string.IsNullOrEmpty(title))
        {
            errors["title"] = ["Title is required."];
        }
        else if (title.Length > TitleMaxLength)
        {
            errors["title"] = [$"Title must be at most {TitleMaxLength} characters."];
        }

        if (description is { Length: > DescriptionMaxLength })
        {
            errors["description"] = [$"Description must be at most {DescriptionMaxLength} characters."];
        }

        // The null check is redundant at runtime (null already added an error) but lets the compiler prove it.
        return title is not null && errors.Count == 0;
    }
}
