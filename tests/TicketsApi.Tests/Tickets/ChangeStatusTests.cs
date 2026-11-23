using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TicketsApi.Features.Tickets;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Tickets;

// T-003 criteria 1-19, 22, 23 and 27. Every test creates its own tickets, so sharing one host is safe.
// Unreadable bodies, 415 and the body cap (criteria 20, 21) are in ChangeStatusLimitsTests (real Kestrel).
// The races (criteria 24, 25) are in ChangeStatusConcurrencyTests.
public class ChangeStatusTests(TicketsApiFactory factory)
    : IClassFixture<TicketsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly string[] Statuses = ["Open", "InProgress", "Resolved", "InReview", "Closed"];

    // ---- Statuses (AC1) ----

    [Fact]
    public void TicketStatus_has_the_five_values_in_order()
    {
        Assert.Equal(Statuses, Enum.GetNames<TicketStatus>());
    }

    // ---- Happy path (AC3) ----

    [Fact]
    public async Task Patch_open_to_in_progress_returns_200_with_full_ticket()
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await _client.CreateTicketAsync("Printer jammed", "Floor 3");
        var id = created.GetProperty("id").GetGuid();

        var response = await _client.PatchStatusAsync(id, new { status = "InProgress" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(response, ct);
        Assert.Equal(
            ["id", "title", "description", "status", "createdAt"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(id, body.GetProperty("id").GetGuid());
        Assert.Equal("Printer jammed", body.GetProperty("title").GetString());
        Assert.Equal("Floor 3", body.GetProperty("description").GetString());
        Assert.Equal(created.GetProperty("createdAt").GetRawText(), body.GetProperty("createdAt").GetRawText());
        Assert.Equal("InProgress", body.GetProperty("status").GetString());
    }

    // ---- Allowed transitions (AC1, AC4, AC5) ----

    public static TheoryData<string, string> AllowedTransitions => new()
    {
        { "Open", "InProgress" },
        { "Open", "Closed" },
        { "InProgress", "Resolved" },
        { "Resolved", "Closed" },
        { "Resolved", "InProgress" },
        { "Resolved", "InReview" },
        { "InReview", "Closed" },
        { "InReview", "InProgress" },
    };

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public async Task Allowed_transition_returns_200_with_new_status(string from, string to)
    {
        var id = await _client.CreateTicketInStatusAsync(from);

        var response = await _client.PatchStatusAsync(id, new { status = to });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal(to, body.GetProperty("status").GetString()); // canonical name, never a number (AC1)
    }

    // ---- GET after PATCH (AC6) ----

    [Fact]
    public async Task Get_after_patch_shows_new_status_and_other_fields_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await _client.CreateTicketAsync("Imprimante bloquée 🖨️", "Café renversé ☕");
        var id = created.GetProperty("id").GetGuid();

        var patch = await _client.PatchStatusAsync(id, new { status = "Closed" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var body = await ReadJsonAsync(await _client.GetAsync($"/tickets/{id}", ct), ct);

        Assert.Equal("Closed", body.GetProperty("status").GetString());
        foreach (var field in new[] { "id", "title", "description", "createdAt" })
        {
            Assert.Equal(created.GetProperty(field).GetRawText(), body.GetProperty(field).GetRawText());
        }
    }

    // ---- Same status is a no-op (AC7) ----

    [Theory]
    [InlineData("Open")]
    [InlineData("InProgress")]
    [InlineData("Resolved")]
    [InlineData("InReview")]
    [InlineData("Closed")]
    public async Task Same_status_is_a_noop_200(string status)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await _client.CreateTicketInStatusAsync(status);
        var before = await (await _client.GetAsync($"/tickets/{id}", ct)).Content.ReadAsStringAsync(ct);

        var response = await _client.PatchStatusAsync(id, new { status });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await response.Content.ReadAsStringAsync(ct));
        Assert.Equal(before, await (await _client.GetAsync($"/tickets/{id}", ct)).Content.ReadAsStringAsync(ct));
    }

    // ---- Invalid transitions (AC8, AC9, AC10, AC11) ----

    public static TheoryData<string, string> DisallowedTransitions => new()
    {
        { "Open", "Resolved" },
        { "Open", "InReview" },
        { "InProgress", "Open" },
        { "InProgress", "Closed" },
        { "InProgress", "InReview" },
        { "Resolved", "Open" },
        { "InReview", "Open" },
        { "InReview", "Resolved" },
        { "Closed", "Open" },
        { "Closed", "InProgress" },
        { "Closed", "Resolved" },
        { "Closed", "InReview" },
    };

    [Theory]
    [MemberData(nameof(DisallowedTransitions))]
    public async Task Disallowed_transition_returns_409_and_leaves_ticket_unchanged(string from, string to)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await _client.CreateTicketInStatusAsync(from);
        var before = await (await _client.GetAsync($"/tickets/{id}", ct)).Content.ReadAsStringAsync(ct);

        var response = await _client.PatchStatusAsync(id, new { status = to });

        var problem = await AssertProblemDetailsAsync(response, HttpStatusCode.Conflict);
        Assert.Equal(from, problem.GetProperty("currentStatus").GetString());
        Assert.Equal(to, problem.GetProperty("requestedStatus").GetString());
        Assert.False(problem.TryGetProperty("allowedStatuses", out _), "409 must not list allowed statuses.");

        Assert.Equal(before, await (await _client.GetAsync($"/tickets/{id}", ct)).Content.ReadAsStringAsync(ct));
    }

    // AC10: the 409 names the canonical status even when the request used another case.
    [Fact]
    public async Task Conflict_reports_canonical_requested_status()
    {
        var id = await _client.CreateTicketInStatusAsync("Open");

        var response = await _client.PatchStatusAsync(id, new { status = "resolved" });

        var problem = await AssertProblemDetailsAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("Resolved", problem.GetProperty("requestedStatus").GetString());
    }

    // Allowed + disallowed + same-status cases together cover every (from, to) pair exactly once.
    [Fact]
    public void Transition_cases_cover_every_pair()
    {
        var pairs = AllowedTransitions.Concat(DisallowedTransitions)
            .Select(row => row.Data)
            .Concat(Statuses.Select(s => (s, s)))
            .ToList();

        var all = Statuses.SelectMany(f => Statuses.Select(t => (f, t))).ToList();
        Assert.Equal(all.Count, pairs.Count);
        Assert.Equal(all.OrderBy(p => p), pairs.OrderBy(p => p));
    }

    // ---- Request validation (AC11-AC19) ----

    [Theory]
    [InlineData("inprogress", "Open", "InProgress")]
    [InlineData("INPROGRESS", "Open", "InProgress")]
    [InlineData("inReview", "Resolved", "InReview")]
    [InlineData("closed", "Open", "Closed")]
    public async Task Status_match_is_case_insensitive_and_response_is_canonical(
        string requested, string from, string expected)
    {
        var id = await _client.CreateTicketInStatusAsync(from);

        var response = await _client.PatchStatusAsync(id, new { status = requested });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal(expected, body.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("""{}""")]                                         // AC13 missing
    [InlineData("""{ "status": null }""")]                         // AC13 null
    [InlineData("""{ "status": "Banana" }""")]                     // AC14
    [InlineData("""{ "status": "In Progress" }""")]                // AC14
    [InlineData("""{ "status": "in_progress" }""")]                // AC14
    [InlineData("""{ "status": "Cloſed" }""")]                     // AC14 long s (U+017F), not "s"
    [InlineData("""{ "status": "ınProgress" }""")]                 // AC14 dotless i (U+0131)
    [InlineData("""{ "status": "Ópen" }""")]                       // AC14 accented
    [InlineData("""{ "status": "Closed​" }""")]               // AC14 zero-width space
    [InlineData("""{ "status": 1 }""")]                            // AC15 JSON number
    [InlineData("""{ "status": 0 }""")]                            // AC15
    [InlineData("""{ "status": "1" }""")]                          // AC15 numeric string
    [InlineData("""{ "status": "99" }""")]                         // AC15
    [InlineData("""{ "status": "" }""")]                           // AC16 empty
    [InlineData("""{ "status": " InProgress" }""")]                // AC16 padded
    [InlineData("""{ "status": "InProgress " }""")]                // AC16 padded
    [InlineData("""{ "status": true }""")]                         // AC17 wrong type
    [InlineData("""{ "status": ["InProgress"] }""")]               // AC17
    [InlineData("""{ "status": { "value": "InProgress" } }""")]    // AC17
    [InlineData("""{ "status": "InProgress", "status": "Closed" }""")] // AC18 repeated key
    [InlineData("""{ "status": "InProgress", "Status": "InProgress" }""")] // AC18 same key, other case
    public async Task Invalid_status_returns_400_naming_status_and_leaves_ticket_unchanged(string json)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await _client.CreateTicketInStatusAsync("Open");
        var before = await (await _client.GetAsync($"/tickets/{id}", ct)).Content.ReadAsStringAsync(ct);

        var response = await _client.PatchStatusRawAsync(id.ToString(), json);

        await AssertValidationErrorAsync(response, "status");
        Assert.Equal(before, await (await _client.GetAsync($"/tickets/{id}", ct)).Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task Unknown_body_fields_are_ignored()
    {
        var id = await _client.CreateTicketInStatusAsync("Open");

        var response = await _client.PatchStatusRawAsync(
            id.ToString(), """{ "status": "InProgress", "reason": "picked up", "priority": 1 }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal("InProgress", body.GetProperty("status").GetString());
    }

    // ---- Id and order of checks (AC22, AC23) ----

    [Fact]
    public async Task Unknown_guid_returns_404_problem_details()
    {
        var response = await _client.PatchStatusAsync(Guid.NewGuid(), new { status = "InProgress" });

        await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("00000000-0000-0000-0000-00000000000Z")]
    public async Task Non_guid_id_returns_404_problem_details(string id)
    {
        var response = await _client.PatchStatusRawAsync(id, """{ "status": "InProgress" }""");

        await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unknown_id_with_invalid_body_returns_400_not_404()
    {
        var response = await _client.PatchStatusRawAsync(Guid.NewGuid().ToString(), """{ "status": "Banana" }""");

        await AssertValidationErrorAsync(response, "status");
    }

    [Fact]
    public async Task Body_is_validated_before_the_transition_check()
    {
        // A Closed ticket rejects every change with 409, but an invalid body must still get 400.
        var id = await _client.CreateTicketInStatusAsync("Closed");

        var response = await _client.PatchStatusRawAsync(id.ToString(), """{ "status": 1 }""");

        await AssertValidationErrorAsync(response, "status");
    }
}
