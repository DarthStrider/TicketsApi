using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

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

        group.MapPatch("{id:guid}/status", ChangeStatusAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes));

        return app;
    }

    // How many writes a status change tries before giving up with 409 (T-003 criterion 25).
    private const int MaxStatusWriteAttempts = 3;

    private static readonly string StatusMustBeOneOf =
        $"Status must be one of: {string.Join(", ", Enum.GetNames<TicketStatus>())}.";

    // Checks run in the order T-003 criterion 23 requires: route (404), content type (415), body (400),
    // lookup (404), transition (409).
    // The body is read here, not bound by the framework, for two reasons:
    // - The content type must be checked first. Minimal APIs accept any "+json" type and would parse
    //   (and reject with 400) an application/merge-patch+json body before this handler runs.
    // - Every bad status (number, wrong type, repeated key) must get a 400 naming `status`. Binding straight to
    //   TicketStatus would accept numbers and give generic errors.
    private static async Task<Results<Ok<TicketResponse>, ValidationProblem, NotFound, ProblemHttpResult>>
        ChangeStatusAsync(
            Guid id,
            HttpRequest request,
            ITicketStore store,
            CancellationToken cancellationToken)
    {
        if (!IsPlainJson(request.ContentType))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        TicketStatus requested;
        try
        {
            // The size cap (RequestSizeLimit) is enforced while reading: too large throws a 413 BadHttpRequestException.
            // The body is read as UTF-8, the encoding JSON requires (RFC 8259); a charset parameter is not transcoded.
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            if (!TryReadRequestedStatus(document.RootElement, out requested, out var errors))
            {
                return TypedResults.ValidationProblem(errors);
            }
        }
        catch (JsonException)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = ["The request body must be a valid JSON object."],
            });
        }

        // Read, check, then compare-and-swap. If another change wins in between, re-read and evaluate again
        // (T-003 criterion 25), so every answer reflects a status the ticket actually had.
        // The loop ends: at most MaxStatusWriteAttempts writes, and the read after the last lost write always returns.
        for (var attempt = 1; ; attempt++)
        {
            var ticket = await store.GetAsync(id, cancellationToken);
            if (ticket is null)
            {
                return TypedResults.NotFound();
            }

            // The same status again is a no-op (T-003 criterion 7).
            if (ticket.Status == requested)
            {
                return TypedResults.Ok(ToResponse(ticket));
            }

            // Not allowed, or allowed but every write so far lost to a concurrent change.
            if (!TicketTransitions.IsAllowed(ticket.Status, requested) || attempt > MaxStatusWriteAttempts)
            {
                return TransitionConflict(ticket.Status, requested);
            }

            var updated = await store.TryUpdateStatusAsync(id, ticket.Status, requested, cancellationToken);
            if (updated is not null)
            {
                return TypedResults.Ok(ToResponse(updated));
            }
        }
    }

    // currentStatus and requestedStatus explain the 409 without fixed text, which T-004 localizes (criterion 10).
    private static ProblemHttpResult TransitionConflict(TicketStatus current, TicketStatus requested) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?>
            {
                ["currentStatus"] = current.ToString(),
                ["requestedStatus"] = requested.ToString(),
            });

    private static bool IsPlainJson(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
        && string.Equals(mediaType.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase);

    // T-003 criteria 12-19. Property names match case-insensitively, like the rest of the API's JSON binding,
    // so "status" and "Status" together count as a repeated key.
    private static bool TryReadRequestedStatus(
        JsonElement body, out TicketStatus status, out Dictionary<string, string[]> errors)
    {
        status = default;
        errors = new Dictionary<string, string[]>();

        if (body.ValueKind != JsonValueKind.Object)
        {
            errors["body"] = ["The request body must be a valid JSON object."];
            return false;
        }

        var values = body.EnumerateObject()
            .Where(p => string.Equals(p.Name, "status", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value)
            .ToList();

        string? error = values switch
        {
            [] => "Status is required.",
            [{ ValueKind: JsonValueKind.Null }] => "Status is required.",
            [{ ValueKind: not JsonValueKind.String }] => StatusMustBeOneOf,
            [var value] when !TryParseStatus(value.GetString()!, out status) => StatusMustBeOneOf,
            [_] => null,
            _ => "Status must appear only once.",
        };

        if (error is not null)
        {
            errors["status"] = [error];
            return false;
        }

        return true;
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
                    ["status"] = [StatusMustBeOneOf],
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
