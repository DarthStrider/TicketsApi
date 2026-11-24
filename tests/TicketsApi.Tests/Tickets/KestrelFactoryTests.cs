namespace TicketsApi.Tests.Tickets;

public class KestrelFactoryTests
{
    [Fact]
    public async Task Binds_a_dynamic_port_so_instances_can_run_in_parallel()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using KestrelFactory first = new();
        await using KestrelFactory second = new();
        first.StartServer();
        second.StartServer();

        using HttpClient firstClient = first.CreateClient();
        using HttpClient secondClient = second.CreateClient();
        int firstPort = firstClient.BaseAddress!.Port;
        int secondPort = secondClient.BaseAddress!.Port;

        Assert.NotEqual(0, firstPort);
        Assert.NotEqual(firstPort, secondPort);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await firstClient.GetAsync("/health", ct)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await secondClient.GetAsync("/health", ct)).StatusCode);

        TestContext.Current.SendDiagnosticMessage($"KestrelFactory ports: {firstPort}, {secondPort}");
    }
}
