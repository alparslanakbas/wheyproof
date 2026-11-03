using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// What Shopify's bot review asked for (2026-11-02, support ticket 71441774): every request to a store
/// with the wheyproofbot User-Agent and signed, none with a browser User-Agent, and no repeat of a 429
/// from another address (the home tunnel is gone).
/// </summary>
public class ShopifyClientIdentityTests
{
    private static ServiceProvider Services(IEnumerable<KeyValuePair<string, string?>> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider();
    }

    private static IConfigurationRoot Production() => new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json")
        .AddJsonFile("appsettings.Production.json")
        .Build();

    [Theory]
    [InlineData("US")]
    [InlineData("UK")]
    public void The_Shopify_client_checks_robots_txt_then_signs_and_nothing_else(string market)
    {
        using var sp = Services([new("Market:Code", market)]);
        var chain = new List<Type>();
        for (var handler = sp.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(ShopifyStoreScraper.HttpClientName);
             handler is DelegatingHandler wrapper; handler = wrapper.InnerHandler!)
            chain.Add(wrapper.GetType());

        // The factory adds its own logging handlers around ours; only ours are asserted.
        Assert.Equal([typeof(RobotsTxtHandler), typeof(WebBotAuthHandler)],
            chain.Where(t => t.Namespace == typeof(WebBotAuthHandler).Namespace).ToList());
    }

    [Fact]
    public void Production_signs_with_the_bot_user_agent()
    {
        var production = Production();
        using var sp = Services(production.AsEnumerable());

        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(ShopifyStoreScraper.HttpClientName);

        Assert.True(production.GetValue<bool>("WebBotAuth:SignRequests"));
        Assert.Contains("wheyproofbot/1.0", client.DefaultRequestHeaders.UserAgent.ToString());
        Assert.DoesNotContain("Chrome", client.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Null(production["Shopify:Tunnel"]);
    }

    [Fact]
    public void Without_settings_the_Shopify_client_still_names_the_bot()
    {
        using var sp = Services([]);

        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(ShopifyStoreScraper.HttpClientName);

        Assert.Contains("wheyproofbot/1.0", client.DefaultRequestHeaders.UserAgent.ToString());
    }
}
