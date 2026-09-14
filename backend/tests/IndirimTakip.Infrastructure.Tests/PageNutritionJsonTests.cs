using IndirimTakip.Infrastructure.NutritionLabels;
using IndirimTakip.Infrastructure.Scraping.Shopify;

namespace IndirimTakip.Infrastructure.Tests;

// Shaped like Naked Nutrition product pages (2026-09-15).
public class PageNutritionJsonTests
{
    private const string WheyObject = """
        {"calcium-amount":"111","calorie":"160","carbohydrate":"11","fat":"2.5","fiber":"3",
         "ingredientList":"Whey Protein Concentrate, Coconut Sugar {organic}","protein":"25",
         "serving_size":"2 Scoops (43g)",
         "entry_1":{"amount":"160","title":"Calories:","dv":"-"},"entry_10":{"amount":"25g","title":"Protein","dv":"50%"}}
        """;

    private static string Page(string handle, string nutritionJson, string otherHandle = "naked-pb", string otherJson = """{"calorie":"999","protein":"99","fat":"9","carbohydrate":"9"}""") => $$"""
        <html><body>
        <script>var related = { "products":[ { "handle":"{{otherHandle}}", "title":"Other", "nutrition": {{otherJson}} } ] };</script>
        <p>lots of page content</p>
        <script>productMetadata = { "products":[ { "handle":"{{handle}}", "title":"This product", "nutrition": {{nutritionJson}} } ] };</script>
        </body></html>
        """;

    [Fact]
    public void Reads_the_object_this_product_owns()
    {
        var reading = PageNutritionJson.Read(Page("double-chocolate-whey-protein-2lb", WheyObject), "double-chocolate-whey-protein-2lb");

        Assert.NotNull(reading);
        Assert.Equal(160m, reading.Calories);
        Assert.Equal(25m, reading.ProteinGrams);
        Assert.Equal(2.5m, reading.FatGrams);
        Assert.Equal(11m, reading.CarbohydrateGrams);
        Assert.Equal(43m, reading.ServingSizeGrams);
        Assert.Contains(reading.Rows, r => r.Label == "Protein" && r.Amount == "25g");
    }

    // Another product's object sits earlier on the page (999 calories); it must
    // never be read for this one.
    [Fact]
    public void Another_products_object_is_ignored() =>
        Assert.Null(PageNutritionJson.Read(Page("some-other-handle", WheyObject), "double-chocolate-whey-protein-2lb"));

    // Naked's creatine capsules page described "1 Stick (5g)" with enable false.
    [Theory]
    [InlineData("\"false\"")]
    [InlineData("false")]
    public void Disabled_objects_are_skipped(string enable) =>
        Assert.Null(PageNutritionJson.Read(
            Page("h", WheyObject.Replace("\"calcium-amount\"", $"\"enable\":{enable},\"calcium-amount\"")), "h"));

    // Colostrum: "<1" is not a number, so there is nothing to check or publish.
    [Fact]
    public void Non_numeric_values_leave_the_panel_unpublished() =>
        Assert.Null(PageNutritionJson.Read(
            Page("h", """{"calorie":"0","protein":"<1","carbohydrate":"<1","serving_size":"About 1 Scoop (1g)"}"""), "h"));

    [Theory]
    [InlineData("https://www.nakednutrition.com/products/double-chocolate-whey-protein-2lb?variant=123", "double-chocolate-whey-protein-2lb")]
    [InlineData("https://www.optimumnutrition.com/en-us/products/gold-standard-100-whey-protein-powder", "gold-standard-100-whey-protein-powder")]
    public void Handle_comes_from_the_product_url(string url, string expected) =>
        Assert.Equal(expected, ShopifyStoreScraper.HandleFrom(url));
}
