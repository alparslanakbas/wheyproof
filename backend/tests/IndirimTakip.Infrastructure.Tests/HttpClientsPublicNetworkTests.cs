using IndirimTakip.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// The SSRF guard (PublicNetworkConnection) is put on every client BY DEFAULT;
/// but a client choosing its own handler (ConfigurePrimaryHttpMessageHandler)
/// replaces the default and silently loses the guard unless it sets it up
/// itself (as the PrestaShop client does today). This test builds the real
/// handler chain of every registered client and checks that the innermost
/// handler connects through PublicNetworkConnection (security review, 27 Sept).
/// The cache-warming client lives in the Api and stays out on purpose: it goes
/// to localhost and isn't registered here.
/// </summary>
public class HttpClientsPublicNetworkTests
{
    private static (List<string> Unguarded, int ClientCount) Scan(IServiceCollection services)
    {
        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpMessageHandlerFactory>();

        // Anything that can replace the handler is a named client configuration;
        // a client without one gets the (guarded) default anyway.
        var names = sp.GetServices<IConfigureOptions<HttpClientFactoryOptions>>()
            .OfType<ConfigureNamedOptions<HttpClientFactoryOptions>>()
            .Select(o => o.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct()
            .ToList();

        var unguarded = new List<string>();
        foreach (var name in names)
        {
            var handler = factory.CreateHandler(name!);
            while (handler is DelegatingHandler wrapper)
                handler = wrapper.InnerHandler!;
            // "ConnectCallback is set" isn't enough: any other callback is set too.
            if (handler is not SocketsHttpHandler { ConnectCallback: { } callback }
                || callback.Method.DeclaringType != typeof(PublicNetworkConnection))
                unguarded.Add($"{name} ({handler.GetType().Name})");
        }
        return (unguarded, names.Count);
    }

    private static ServiceCollection InfrastructureFor(string market)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Market:Code"] = market })
            .Build());
        return services;
    }

    [Theory]
    [InlineData("US")]
    [InlineData("UK")]
    public void Every_registered_client_connects_through_the_public_network_guard(string market)
    {
        var (unguarded, count) = Scan(InfrastructureFor(market));

        // If the counting breaks, the test must not pass as "no clients, all clean".
        Assert.True(count >= 5, $"only {count} clients found; the counting may be broken");
        Assert.Empty(unguarded);
    }

    [Fact]
    public void A_client_building_its_own_unguarded_handler_is_caught()
    {
        // Negative control, kept: if someone writes "new SocketsHttpHandler()"
        // tomorrow, the test above must see exactly this.
        var services = InfrastructureFor("US");
        services.AddHttpClient("unguarded-probe")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler());

        var (unguarded, _) = Scan(services);

        Assert.Equal(["unguarded-probe (SocketsHttpHandler)"], unguarded);
    }
}
