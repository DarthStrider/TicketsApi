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

        return app;
    }

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

        var response = new TicketResponse(
            ticket.Id, ticket.Title, ticket.Description, ticket.Status, ticket.CreatedAt);

        return TypedResults.Created($"/tickets/{ticket.Id}", response);
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
