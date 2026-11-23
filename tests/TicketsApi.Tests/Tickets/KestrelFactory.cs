using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace TicketsApi.Tests.Tickets;

// The in-memory TestServer doesn't enforce request body size limits, so tests for them run on real Kestrel.
// Port 0 lets the OS pick a free port, so several factories can run in parallel.
//
// The port goes in the URL setting, not in UseKestrel(0). UseKestrel(port) makes the factory clear and rewrite
// the server addresses after the host is built, but with a top-level Program.cs the app's own app.Run() starts
// Kestrel on another thread, so that rewrite can come too late and throw "Addresses cannot be modified after the
// server has started". Kestrel reads the URL setting before it starts, so there is nothing to race.
// PreferHostingUrls makes this URL win over any Kestrel:Endpoints config or Listen* calls the app might add later.
public class KestrelFactory : WebApplicationFactory<Program>
{
    public KestrelFactory()
    {
        UseKestrel();
    }

    protected virtual string Environment => Environments.Development;

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder
            .UseEnvironment(Environment)
            .UseUrls("http://127.0.0.1:0")
            .UseSetting(WebHostDefaults.PreferHostingUrlsKey, "true");
}

// Production differs from Development: endpoints don't throw on bad requests and there's no developer exception page.
public sealed class ProductionKestrelFactory : KestrelFactory
{
    protected override string Environment => Environments.Production;
}
