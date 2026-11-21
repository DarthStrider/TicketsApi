namespace TicketsApi.Tests.Tickets;

public class KestrelFactoryTests
{
    [Fact]
    public async Task Binds_a_dynamic_port_so_instances_can_run_in_parallel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var first = new KestrelFactory();
        await using var second = new KestrelFactory();
        first.StartServer();
        second.StartServer();

        using var firstClient = first.CreateClient();
        using var secondClient = second.CreateClient();
        var firstPort = firstClient.BaseAddress!.Port;
        var secondPort = secondClient.BaseAddress!.Port;

        Assert.NotEqual(0, firstPort);
        Assert.NotEqual(firstPort, secondPort);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await firstClient.GetAsync("/health", ct)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await secondClient.GetAsync("/health", ct)).StatusCode);

        TestContext.Current.SendDiagnosticMessage($"KestrelFactory ports: {firstPort}, {secondPort}");
    }
}
