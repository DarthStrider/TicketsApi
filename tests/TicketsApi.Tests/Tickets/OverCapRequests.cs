using System.Net.Http.Headers;
using System.Text;

namespace TicketsApi.Tests.Tickets;

// Sends a request whose body is over the size cap to a real Kestrel server, so the test can always read the 413.
//
// Without help, the client uploads the body while Kestrel, which already knows from Content-Length that it's too
// large, answers 413 and closes the connection with the body unread. Windows then resets the connection, and the
// client sometimes gets "connection aborted" instead of the 413: a flaky test, not an API bug.
//
// With "Expect: 100-continue" the client sends only the headers and waits. Kestrel checks Content-Length against the
// limit when the body read starts, before it would send "100 Continue", so it answers 413 and the body is never sent.
// The client waits up to ContinueTimeout (HttpClient's default is 1 s) so a slow server under load can't make it
// upload anyway. If the limit were missing, Kestrel would send "100 Continue", the body would be read, and the
// test would get a 2xx and fail, as it should.
internal static class OverCapRequests
{
    private static readonly TimeSpan ContinueTimeout = TimeSpan.FromSeconds(30);

    private static readonly HttpClient Client = new(new SocketsHttpHandler { Expect100ContinueTimeout = ContinueTimeout });

    // `server` is a client from the Kestrel factory; only its base address is used.
    public static Task<HttpResponseMessage> SendAsync(
        HttpClient server, HttpMethod method, string url, string json, string? acceptLanguage = null)
    {
        HttpRequestMessage request = new(method, new Uri(server.BaseAddress!, url))
        {
            Content = new StringContent(json, Encoding.UTF8), // Content-Length is set, as for any request
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.ExpectContinue = true;
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        return Client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
