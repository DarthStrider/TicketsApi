using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Net.Http.Headers;
using TicketsApi.Localization;

namespace TicketsApi.Features.Tickets;

public static class TicketsEndpoints
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;

    // The largest valid body, fully \u-escaped, is about 25 KB, so 32 KB never rejects a valid ticket.
    public const long MaxRequestBodyBytes = 32 * 1024;

    public static IEndpointRouteBuilder MapTicketsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/tickets");

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
            IStringLocalizer<Messages> localizer,
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
            using JsonDocument document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            if (!TryReadRequestedStatus(document.RootElement, localizer, out requested, out Dictionary<string, string[]> errors))
            {
                return TypedResults.ValidationProblem(errors);
            }
        }
        catch (JsonException)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = [localizer["BodyInvalid"]],
            });
        }

        // Read, check, then compare-and-swap. If another change wins in between, re-read and evaluate again
        // (T-003 criterion 25), so every answer reflects a status the ticket actually had.
        // The loop ends: at most MaxStatusWriteAttempts writes, and the read after the last lost write always returns.
        for (int attempt = 1; ; attempt++)
        {
            Ticket? ticket = await store.GetAsync(id, cancellationToken);
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

            Ticket? updated = await store.TryUpdateStatusAsync(id, ticket.Status, requested, cancellationToken);
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
        MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? mediaType)
        && string.Equals(mediaType.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase);

    // T-003 criteria 12-19. Property names match case-insensitively, like the rest of the API's JSON binding,
    // so "status" and "Status" together count as a repeated key.
    private static bool TryReadRequestedStatus(
        JsonElement body,
        IStringLocalizer<Messages> localizer,
        out TicketStatus status,
        out Dictionary<string, string[]> errors)
    {
        status = default;
        errors = new Dictionary<string, string[]>();

        if (body.ValueKind != JsonValueKind.Object)
        {
            errors["body"] = [localizer["BodyInvalid"]];
            return false;
        }

        List<JsonElement> values = body.EnumerateObject()
            .Where(p => string.Equals(p.Name, "status", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value)
            .ToList();

        string? key = values switch
        {
            [] => "StatusRequired",
            [{ ValueKind: JsonValueKind.Null }] => "StatusRequired",
            [{ ValueKind: not JsonValueKind.String }] => "StatusMustBeOneOf",
            [JsonElement value] when !TryParseStatus(value.GetString()!, out status) => "StatusMustBeOneOf",
            [_] => null,
            _ => "StatusRepeated",
        };

        if (key is not null)
        {
            errors["status"] = [localizer[key]];
            return false;
        }

        return true;
    }

    private static async Task<Results<Ok<TicketResponse>, NotFound>> GetTicketAsync(
        Guid id,
        ITicketStore store,
        CancellationToken cancellationToken)
    {
        Ticket? ticket = await store.GetAsync(id, cancellationToken);

        // The empty 404 becomes ProblemDetails through AddProblemDetails + UseStatusCodePages in Program.cs.
        return ticket is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ToResponse(ticket));
    }

    // Bound as an array so a repeated ?status= can be detected and rejected (T-002 criterion 15).
    private static async Task<Results<Ok<TicketListResponse>, ValidationProblem>> ListTicketsAsync(
        [FromQuery] string[]? status,
        ITicketStore store,
        IStringLocalizer<Messages> localizer,
        CancellationToken cancellationToken)
    {
        TicketStatus? filter = null;
        if (status is { Length: > 0 })
        {
            if (status.Length > 1 || !TryParseStatus(status[0], out TicketStatus parsed))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [localizer["StatusMustBeOneOf"]],
                });
            }

            filter = parsed;
        }

        IReadOnlyList<Ticket> tickets = await store.ListAsync(filter, cancellationToken);

        return TypedResults.Ok(new TicketListResponse([.. tickets.Select(ToResponse)]));
    }

    // Matches only defined status names, ignoring case. Enum.TryParse is not used on its own because it
    // accepts numeric strings, even undefined ones like "99", and also trims spaces and accepts "A,B" lists.
    private static bool TryParseStatus(string value, out TicketStatus status)
    {
        foreach (TicketStatus candidate in Enum.GetValues<TicketStatus>())
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
        IStringLocalizer<Messages> localizer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        string? title = request.Title?.Trim();
        if (!IsValid(title, request.Description, localizer, out Dictionary<string, string[]> errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        Ticket ticket = new()
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
        IStringLocalizer<Messages> localizer,
        out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>();

        if (string.IsNullOrEmpty(title))
        {
            errors["title"] = [localizer["TitleRequired"]];
        }
        else if (title.Length > TitleMaxLength)
        {
            errors["title"] = [localizer["TitleTooLong"]];
        }

        if (description is { Length: > DescriptionMaxLength })
        {
            errors["description"] = [localizer["DescriptionTooLong"]];
        }

        // The null check is redundant at runtime (null already added an error) but lets the compiler prove it.
        return title is not null && errors.Count == 0;
    }
}
