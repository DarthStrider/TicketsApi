using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace TicketsApi.Tests.Tickets;

// The in-memory TestServer doesn't enforce request body size limits, so tests for them run on real Kestrel.
// Port 0 lets the OS pick a free port, so several factories can run in parallel.
public class KestrelFactory : WebApplicationFactory<Program>
{
    public KestrelFactory()
    {
        UseKestrel(0);
    }

    protected virtual string Environment => Environments.Development;

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment(Environment);
}

// Production differs from Development: endpoints don't throw on bad requests and there's no developer exception page.
public sealed class ProductionKestrelFactory : KestrelFactory
{
    protected override string Environment => Environments.Production;
}
