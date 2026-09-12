using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

// The URL reported to IndexNow for a new product. It used to be built on the
// Turkish site's /urun/ path, which returns 404 on this site; the route has to
// match the frontend's product/:id/:slug.
public class ProductPageUrlTests
{
    [Theory]
    [InlineData("https://www.wheyproof.com")]
    [InlineData("https://www.wheyproof.com/")]
    public void Builds_the_frontend_product_route(string baseUrl)
    {
        var url = ScrapeIngestionService.ProductPageUrl(baseUrl, 7539, "Creatine Gummies - 12 Serv");

        Assert.Equal("https://www.wheyproof.com/product/7539/creatine-gummies-12-serv", url);
    }
}
