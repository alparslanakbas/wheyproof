using System.Net;
using System.Net.Sockets;
using System.Text;
using IndirimTakip.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// SSRF protection: clients that request addresses from the sources must not
/// connect to the internal network (security review, 2026-09-26).
/// </summary>
public class PublicNetworkConnectionTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.17.0.2")]        // Docker bridge network
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]   // cloud instance metadata endpoint
    [InlineData("100.64.0.1")]        // CGNAT
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:127.0.0.1")]  // loopback dressed as IPv6
    [InlineData("::ffff:169.254.169.254")]
    public void Internal_addresses_are_rejected(string address)
    {
        Assert.False(PublicNetworkConnection.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("104.16.0.1")]
    [InlineData("172.32.0.1")]        // just outside 172.16/12
    [InlineData("100.128.0.1")]       // just outside CGNAT
    [InlineData("2606:4700::1111")]
    public void Public_addresses_are_accepted(string address)
    {
        Assert.True(PublicNetworkConnection.IsPublic(IPAddress.Parse(address)));
    }

    /// <summary>
    /// The real proof: a server is actually listening locally. A plain handler
    /// reaches it (the control showing the test measures something real), the
    /// protected handler doesn't even connect.
    /// </summary>
    [Fact]
    public async Task Protected_client_does_not_connect_to_a_local_server()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = 0;
        var server = Task.Run(async () =>
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync();
                Interlocked.Increment(ref accepted);
                var stream = client.GetStream();
                await stream.ReadAsync(new byte[4096]);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"));
            }
        });

        using (var plain = new HttpClient(new SocketsHttpHandler()))
        {
            Assert.Equal("ok", await plain.GetStringAsync($"http://127.0.0.1:{port}/"));
        }
        Assert.Equal(1, accepted);

        using var guarded = new HttpClient(PublicNetworkConnection.CreateHandler());
        await Assert.ThrowsAsync<HttpRequestException>(
            () => guarded.GetStringAsync($"http://127.0.0.1:{port}/"));
        // By name too: localhost resolves to a loopback address through DNS.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => guarded.GetStringAsync($"http://localhost:{port}/"));

        Assert.Equal(1, accepted);
        listener.Stop();
    }

    /// <summary>
    /// The protection comes to every client by default; a client that goes
    /// internal on purpose (cache warmup) must win when it picks its own handler.
    /// </summary>
    [Fact]
    public void Default_applies_everywhere_and_an_explicit_choice_overrides_it()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(b =>
            b.ConfigurePrimaryHttpMessageHandler(PublicNetworkConnection.CreateHandler));
        services.AddHttpClient("scraper");
        services.AddHttpClient("warmup")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler());
        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpMessageHandlerFactory>();

        Assert.NotNull(PrimaryHandler(factory.CreateHandler("scraper")).ConnectCallback);
        Assert.NotNull(PrimaryHandler(factory.CreateHandler(string.Empty)).ConnectCallback);
        Assert.Null(PrimaryHandler(factory.CreateHandler("warmup")).ConnectCallback);
    }

    private static SocketsHttpHandler PrimaryHandler(HttpMessageHandler handler)
    {
        while (handler is DelegatingHandler chain)
            handler = chain.InnerHandler!;
        return Assert.IsType<SocketsHttpHandler>(handler);
    }
}
