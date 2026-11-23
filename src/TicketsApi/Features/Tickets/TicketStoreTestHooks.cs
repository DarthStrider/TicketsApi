namespace TicketsApi.Features.Tickets;

// Test-only seam (T-003 criteria 24 and 25). It's a singleton, so a test can set it once on the host and every store
// instance sees it, including a per-request EF Core store in T-005.
// Internal, does nothing unless a test sets it, and no endpoint reads or writes it, so it can't be reached over HTTP.
internal sealed class TicketStoreTestHooks
{
    // Runs after a status change has read the ticket and before it writes, so a test can pause a request there.
    public Func<Guid, CancellationToken, Task>? BeforeStatusWriteAsync { get; set; }
}
