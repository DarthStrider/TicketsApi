using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TicketsApi.Features.Tickets;
using static TicketsApi.Tests.Tickets.TicketTestHelpers;

namespace TicketsApi.Tests.Tickets;

// T-003 criterion 24: a status change is an atomic check-and-set.
// T-003 criterion 25: a change that loses the race is re-evaluated against the new status.
// Each test uses its own host, so the hooks it sets can't affect other tests.
public class ChangeStatusConcurrencyTests(TicketsApiFactory factory)
    : IClassFixture<TicketsApiFactory>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // ---- Criterion 24 ----

    [Fact]
    public async Task Conflicting_patches_that_both_read_before_either_writes_exactly_one_wins()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        HttpClient client = app.CreateClient();
        Guid id = await client.CreateTicketInStatusAsync("Resolved");

        // Hold the first two status writes until both have arrived, so both requests have read "Resolved"
        // before either one writes. Later calls (for example a retry after a lost compare-and-swap) pass through.
        int arrived = 0;
        TaskCompletionSource bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Hooks(app).BeforeStatusWriteAsync = async (_, token) =>
        {
            int n = Interlocked.Increment(ref arrived);
            if (n > 2)
            {
                return;
            }

            if (n == 2)
            {
                bothArrived.TrySetResult();
            }

            // A timeout instead of a hang if the second request never reaches the write.
            await bothArrived.Task.WaitAsync(Timeout, token);
        };

        HttpResponseMessage[] responses = await Task.WhenAll(
            client.PatchStatusAsync(id, new { status = "Closed" }),
            client.PatchStatusAsync(id, new { status = "InProgress" }));

        Assert.True(bothArrived.Task.IsCompletedSuccessfully, "Both requests must reach the write together.");

        List<HttpStatusCode> statuses = responses.Select(r => r.StatusCode).OrderBy(s => s).ToList();
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);

        string winner = responses[0].StatusCode == HttpStatusCode.OK ? "Closed" : "InProgress";
        HttpResponseMessage loser = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        JsonElement problem = await AssertProblemDetailsAsync(loser, HttpStatusCode.Conflict);
        Assert.Equal(winner, problem.GetProperty("currentStatus").GetString());

        Assert.Equal(winner, await GetStatusAsync(client, id, ct));
    }

    // ---- Criterion 25: the losing change is re-evaluated ----

    [Fact]
    public async Task Loser_is_reevaluated_and_an_allowed_transition_is_applied()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        HttpClient client = app.CreateClient();
        Guid id = await client.CreateTicketInStatusAsync("Resolved");
        WriteGate gate = new(Hooks(app));

        // X reads Resolved and pauses before writing InProgress. Y moves the ticket to InReview.
        gate.Arm();
        Task<HttpResponseMessage> x = client.PatchStatusAsync(id, new { status = "InProgress" });
        await gate.WaitUntilPausedAsync();
        HttpResponseMessage y = await client.PatchStatusAsync(id, new { status = "InReview" });
        Assert.Equal(HttpStatusCode.OK, y.StatusCode);

        // X loses the compare-and-swap, re-reads InReview, and InReview -> InProgress is allowed.
        gate.Release(rearm: false);
        HttpResponseMessage response = await x;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("InProgress", (await ReadJsonAsync(response, ct)).GetProperty("status").GetString());
        Assert.Equal("InProgress", await GetStatusAsync(client, id, ct));
    }

    [Fact]
    public async Task Two_patches_to_the_same_status_both_get_200()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        HttpClient client = app.CreateClient();
        Guid id = await client.CreateTicketInStatusAsync("Resolved");
        WriteGate gate = new(Hooks(app));

        gate.Arm();
        Task<HttpResponseMessage> x = client.PatchStatusAsync(id, new { status = "Closed" });
        await gate.WaitUntilPausedAsync();
        HttpResponseMessage y = await client.PatchStatusAsync(id, new { status = "Closed" });
        gate.Release(rearm: false);
        HttpResponseMessage response = await x;

        Assert.Equal(HttpStatusCode.OK, y.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Closed", (await ReadJsonAsync(response, ct)).GetProperty("status").GetString());
        Assert.Equal("Closed", await GetStatusAsync(client, id, ct));
    }

    [Fact]
    public async Task Losing_three_times_gets_409_with_the_true_current_status()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        HttpClient client = app.CreateClient();
        Guid id = await client.CreateTicketInStatusAsync("Resolved");
        WriteGate gate = new(Hooks(app));

        // Each time X is about to write Closed, Y changes the ticket Resolved -> InProgress -> Resolved.
        // X sees Resolved again, so Closed is still allowed, but its compare-and-swap keeps losing.
        gate.Arm();
        Task<HttpResponseMessage> x = client.PatchStatusAsync(id, new { status = "Closed" });
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await gate.WaitUntilPausedAsync();
            await MoveAsync(client, id, "InProgress", "Resolved");
            gate.Release(rearm: attempt < 3);
        }

        HttpResponseMessage response = await x;

        JsonElement problem = await AssertProblemDetailsAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("Resolved", problem.GetProperty("currentStatus").GetString());
        Assert.Equal("Closed", problem.GetProperty("requestedStatus").GetString());
        Assert.Equal("Resolved", await GetStatusAsync(client, id, ct));
    }

    // The ticket ends in a status different from X's first read, so a 409 built from a stale read would be caught.
    [Fact]
    public async Task Losing_three_times_reports_the_status_at_the_end_not_the_first_read()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        HttpClient client = app.CreateClient();
        Guid id = await client.CreateTicketInStatusAsync("Resolved");
        WriteGate gate = new(Hooks(app));

        // X (to Closed) first reads Resolved. Closed stays allowed on every re-read, but each write loses,
        // and after the third loss the ticket is InReview.
        string[][] moves = [["InReview"], ["InProgress", "Resolved"], ["InReview"]];
        gate.Arm();
        Task<HttpResponseMessage> x = client.PatchStatusAsync(id, new { status = "Closed" });
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await gate.WaitUntilPausedAsync();
            await MoveAsync(client, id, moves[attempt - 1]);
            gate.Release(rearm: attempt < 3);
        }

        JsonElement problem = await AssertProblemDetailsAsync(await x, HttpStatusCode.Conflict);
        Assert.Equal("InReview", problem.GetProperty("currentStatus").GetString());
        Assert.Equal("Closed", problem.GetProperty("requestedStatus").GetString());
        Assert.Equal("InReview", await GetStatusAsync(client, id, ct));
    }

    [Fact]
    public async Task Losing_three_times_to_the_requested_status_gets_200()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });
        HttpClient client = app.CreateClient();
        Guid id = await client.CreateTicketInStatusAsync("Resolved");
        WriteGate gate = new(Hooks(app));

        // X loses twice to a Resolved -> InProgress -> Resolved cycle, then a third time to Y closing the ticket.
        // The ticket ends in the status X asked for, so X must get 200, never "409: Closed -> Closed".
        gate.Arm();
        Task<HttpResponseMessage> x = client.PatchStatusAsync(id, new { status = "Closed" });
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await gate.WaitUntilPausedAsync();
            if (attempt < 3)
            {
                await MoveAsync(client, id, "InProgress", "Resolved");
            }
            else
            {
                await MoveAsync(client, id, "Closed");
            }

            gate.Release(rearm: attempt < 3);
        }

        HttpResponseMessage response = await x;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Closed", (await ReadJsonAsync(response, ct)).GetProperty("status").GetString());
    }

    [Fact]
    public void Hooks_do_nothing_by_default()
    {
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(_ => { });

        Assert.Null(Hooks(app).BeforeStatusWriteAsync);
    }

    // ---- Helpers ----

    private static TicketStoreTestHooks Hooks(WebApplicationFactory<Program> app) =>
        app.Services.GetRequiredService<TicketStoreTestHooks>();

    private static async Task<string?> GetStatusAsync(HttpClient client, Guid id, CancellationToken ct)
    {
        JsonElement body = await ReadJsonAsync(await client.GetAsync($"/tickets/{id}", ct), ct);
        return body.GetProperty("status").GetString();
    }

    private static async Task MoveAsync(HttpClient client, Guid id, params string[] statuses)
    {
        foreach (string status in statuses)
        {
            HttpResponseMessage response = await client.PatchStatusAsync(id, new { status });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // Pauses exactly one status write (the next one after Arm) until the test releases it. Writes made while the
    // gate is not armed, such as the test's own competing PATCHes, pass straight through.
    private sealed class WriteGate
    {
        private int _armed;
        private TaskCompletionSource _paused = NewSignal();
        private TaskCompletionSource _release = NewSignal();

        public WriteGate(TicketStoreTestHooks hooks) => hooks.BeforeStatusWriteAsync = OnWriteAsync;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public Task WaitUntilPausedAsync() => _paused.Task.WaitAsync(Timeout);

        // Lets the paused write continue. With rearm, the next write pauses again.
        public void Release(bool rearm)
        {
            TaskCompletionSource release = _release;
            _paused = NewSignal();
            _release = NewSignal();
            if (rearm)
            {
                Arm();
            }

            release.TrySetResult();
        }

        private async Task OnWriteAsync(Guid id, CancellationToken token)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 0)
            {
                return;
            }

            TaskCompletionSource release = _release;
            _paused.TrySetResult();
            await release.Task.WaitAsync(Timeout, token);
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
