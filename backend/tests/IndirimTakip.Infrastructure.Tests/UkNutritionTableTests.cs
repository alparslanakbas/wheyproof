using System.Net;
using System.Text;
using System.Text.Json;
using IndirimTakip.Infrastructure.NutritionLabels;
using IndirimTakip.Infrastructure.Scraping.Magento;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndirimTakip.Infrastructure.Tests;

// The tables follow the stores' pages as fetched on 2026-10-07, markup trimmed.
public class UkNutritionTableTests
{
    // Myprotein Impact Whey Isolate, the flavour the record links to: the header and the rows are two
    // tables nested in the cells of an outer one.
    internal const string MyproteinCaptainAmerica = """
        <div id="nutritionalinfo"><table border-collapse="collapse"><tbody><tr></tr>
        <tr><td colspan="1"><table border-collapse="collapse"><tbody><tr></tr>
          <tr><td colspan="3"><span style="font-size: inherit;">NUTRITIONAL INFORMATION</span></td></tr>
          <tr><td colspan="1"><span>Typical Values</span></td><td colspan="1"><span>Per 100g</span></td><td colspan="1"><span>Per 30g</span></td></tr>
        </tbody></table></td></tr>
        <tr><td colspan="1"><table border-collapse="collapse"><tbody><tr></tr>
          <tr><td><span>Energy</span></td><td><span>1470kJ</span></td><td><span>441kJ</span></td></tr>
          <tr><td><span>Energy</span></td><td><span>346kcal</span></td><td><span>104kcal</span></td></tr>
          <tr><td><span>Fat</span></td><td><span>0.9g</span></td><td><span>0g</span></td></tr>
          <tr><td><span>of which saturates</span></td><td><span>0.4g</span></td><td><span>0.1g</span></td></tr>
          <tr><td><span>Carbohydrate</span></td><td><span>4.8g</span></td><td><span>1.4g</span></td></tr>
          <tr><td><span>of which sugars</span></td><td><span>1.9g</span></td><td><span>0.6g</span></td></tr>
          <tr><td><span>Protein</span></td><td><span>80g</span></td><td><span>24g</span></td></tr>
          <tr><td><span>Salt</span></td><td><span>0.63g</span></td><td><span>0.19g</span></td></tr>
        </tbody></table></td></tr></tbody></table></div>
        """;

    // Another flavour of the same product, further down the same page.
    internal const string MyproteinVanilla = """
        <table><tbody><tr><td><table><tbody>
          <tr><td>Typical Values</td><td>Per 100g</td><td>Per 30g</td></tr>
        </tbody></table></td></tr><tr><td><table><tbody>
          <tr><td>Energy</td><td>1533kJ</td><td>460kJ</td></tr>
          <tr><td>Energy</td><td>361kcal</td><td>108kcal</td></tr>
          <tr><td>Fat</td><td>0.9g</td><td>&lt;0.5g</td></tr>
          <tr><td>of which saturates</td><td>0.4g</td><td>0.1g</td></tr>
          <tr><td>Carbohydrate</td><td>6.1g</td><td>1.8g</td></tr>
          <tr><td>of which sugars</td><td>1.9g</td><td>0.6g</td></tr>
          <tr><td>Protein</td><td>82g</td><td>25g</td></tr>
          <tr><td>Salt</td><td>0.65g</td><td>0.20g</td></tr>
        </tbody></table></td></tr></tbody></table>
        """;

    // Bulk Basic Whey Protein: two rows stacked in one cell, energy as "kJ/kcal".
    private const string BulkBasicWhey = """
        <h2 id="nutrition">Nutrition</h2>
        <table>
          <thead><tr><th>Nutrition</th><th>per 100 g</th><th>per serving (30g)</th></tr></thead>
          <tbody>
            <tr><td>Energy kJ/kcal</td><td>1542/363</td><td>463/109</td></tr>
            <tr><td><p>Fat</p><p>of which saturates</p></td><td><p>1.4 g</p><p>0.6 g</p></td><td><p>0.4 g</p><p>0.2 g</p></td></tr>
            <tr><td><p>Carbohydrate</p><p>of which sugars</p></td><td><p>28 g</p><p>25 g</p></td><td><p>8.4 g</p><p>7.5 g</p></td></tr>
            <tr><td>Fibre</td><td>1.4 g</td><td>0.4 g</td></tr>
            <tr><td>Protein</td><td>59 g</td><td>18 g</td></tr>
            <tr><td>Salt</td><td>2.95 g</td><td>0.89 g</td></tr>
          </tbody>
        </table>
        """;

    // Veloforte CollagenPro: the nutrition table, then amino acid and mineral tables with the same columns.
    private const string VeloforteNutrition = """
        <table><tbody>
          <tr><td>Typical Values</td><td>Per 100g</td><td>Per Serve (10g)</td></tr>
          <tr><td>Energy (kJ/kcal)</td><td>1523 / 364</td><td>152 / 36</td></tr>
          <tr><td>Total Fat (g)</td><td>0</td><td>0</td></tr>
          <tr><td>of which Saturates (g)</td><td>0</td><td>0</td></tr>
          <tr><td>Carbohydrates (g)</td><td>0</td><td>0</td></tr>
          <tr><td>of which Sugars (g)</td><td>0</td><td>0</td></tr>
          <tr><td>Fibre (g)</td><td>0.9</td><td>0.1</td></tr>
          <tr><td>Protein (g)</td><td>91</td><td>9.1</td></tr>
          <tr><td>Salt (g)</td><td>0.75</td><td>0.08</td></tr>
          <tr><td>Vitamin C (mg)</td><td>240 (300% NRV)</td><td>24 (30% NRV)</td></tr>
        </tbody></table>
        """;

    private const string VeloforteAminoAcids = """
        <table><tbody>
          <tr><td>Amino Acid (mg)</td><td>Per 100g</td><td>Per 10g Serving</td></tr>
          <tr><td>Alanine</td><td>8600mg</td><td>860mg</td></tr>
          <tr><td>Arginine</td><td>7700mg</td><td>770mg</td></tr>
        </tbody></table>
        """;

    private static Dictionary<string, string> Rows(NutritionLabelReading reading) =>
        reading.Rows.ToDictionary(r => r.Label, r => r.Amount);

    [Fact]
    public void Reads_the_serving_column_of_a_nested_table()
    {
        var reading = UkNutritionTable.Read(MyproteinCaptainAmerica);

        Assert.NotNull(reading);
        Assert.Equal(30m, reading.ServingSizeGrams);
        Assert.Equal(104m, reading.Calories);
        Assert.Equal(24m, reading.ProteinGrams);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Serving Size"] = "30g", ["Energy"] = "441kJ / 104kcal", ["Fat"] = "0g",
                ["of which saturates"] = "0.1g", ["Carbohydrate"] = "1.4g", ["of which sugars"] = "0.6g",
                ["Protein"] = "24g", ["Salt"] = "0.19g",
            },
            Rows(reading));
    }

    // Myprotein prints one table per flavour; a later one never stands in for the first.
    [Fact]
    public void The_first_table_decides()
    {
        var reading = UkNutritionTable.Read(MyproteinCaptainAmerica + MyproteinVanilla);

        Assert.Equal(24m, reading?.ProteinGrams);
        Assert.Null(UkNutritionTable.Read(MyproteinCaptainAmerica.Replace("<span>24g</span>", "<span>34g</span>") + MyproteinVanilla));
    }

    [Fact]
    public void Rows_stacked_in_one_cell_are_paired_line_by_line()
    {
        var reading = UkNutritionTable.Read(BulkBasicWhey);

        Assert.NotNull(reading);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Serving Size"] = "30g", ["Energy"] = "463kJ / 109kcal", ["Fat"] = "0.4g",
                ["of which saturates"] = "0.2g", ["Carbohydrate"] = "8.4g", ["of which sugars"] = "7.5g",
                ["Fibre"] = "0.4g", ["Protein"] = "18g", ["Salt"] = "0.89g",
            },
            Rows(reading));
        Assert.Equal(18m, reading.ProteinGrams);
    }

    // Read from the wrong line, fat (0.4 g per serving) and saturates (0.2 g) swap places: 1.4 g per 100 g
    // can't be 0.2 g in 30 g.
    [Fact]
    public void A_row_that_does_not_scale_to_the_serving_rejects_the_table()
    {
        var swapped = BulkBasicWhey.Replace("<td><p>0.4 g</p><p>0.2 g</p></td>", "<td><p>0.2 g</p><p>0.4 g</p></td>");

        Assert.Null(UkNutritionTable.Read(swapped));
        Assert.Null(UkNutritionTable.Read(BulkBasicWhey.Replace("<td>18 g</td>", "<td>28 g</td>")));
    }

    // The columns agree with each other but the energy doesn't follow from the macros
    // (4 x 59 + 4 x 28 + 9 x 1.4 + 2 x 1.4 is 363 kcal, not 463).
    [Fact]
    public void Energy_that_does_not_follow_from_the_macros_rejects_the_table()
    {
        var wrong = BulkBasicWhey.Replace("<td>1542/363</td><td>463/109</td>", "<td>1937/463</td><td>581/139</td>");

        Assert.Null(UkNutritionTable.Read(wrong));
    }

    [Fact]
    public void Stacked_lines_that_do_not_pair_up_reject_the_table()
    {
        var uneven = BulkBasicWhey.Replace("<td><p>1.4 g</p><p>0.6 g</p></td>", "<td><p>1.4 g</p></td>");

        Assert.Null(UkNutritionTable.Read(uneven));
    }

    [Fact]
    public void Amino_acid_tables_are_passed_over()
    {
        Assert.Equal(9.1m, UkNutritionTable.Read(VeloforteNutrition + VeloforteAminoAcids)?.ProteinGrams);
        // Before the nutrition table too.
        var reading = UkNutritionTable.Read(VeloforteAminoAcids + VeloforteNutrition);
        Assert.NotNull(reading);
        Assert.Equal(10m, reading.ServingSizeGrams);
        Assert.Equal("152kJ / 36kcal", Rows(reading)["Energy"]);
        Assert.Equal("0g", Rows(reading)["Fat"]);
        Assert.False(Rows(reading).ContainsKey("Vitamin C (mg)"));
    }

    [Theory]
    // Nothing to check the serving against.
    [InlineData("<tr><td>Typical Values</td><td>Per 100g</td></tr><tr><td>Energy</td><td>346kcal</td></tr><tr><td>Fat</td><td>0.9g</td></tr><tr><td>Carbohydrate</td><td>4.8g</td></tr><tr><td>Protein</td><td>80g</td></tr>")]
    // A drink per 100 ml: the serving isn't in grams.
    [InlineData("<tr><td>Typical Values</td><td>Per 100ml</td><td>Per 330ml</td></tr><tr><td>Energy (kcal)</td><td>55</td><td>182</td></tr><tr><td>Fat</td><td>1.3g</td><td>4.4g</td></tr><tr><td>Carbohydrate</td><td>2.8g</td><td>9.3g</td></tr><tr><td>Protein</td><td>8.4g</td><td>27.7g</td></tr>")]
    // No protein row.
    [InlineData("<tr><td>Typical Values</td><td>Per 100g</td><td>Per 30g</td></tr><tr><td>Energy</td><td>346kcal</td><td>104kcal</td></tr><tr><td>Fat</td><td>0.9g</td><td>0g</td></tr><tr><td>Carbohydrate</td><td>4.8g</td><td>1.4g</td></tr>")]
    public void Tables_that_cannot_be_checked_are_not_read(string rows)
    {
        Assert.Null(UkNutritionTable.Read($"<table><tbody>{rows}</tbody></table>"));
    }

    [Fact]
    public async Task A_uk_shopify_store_reads_the_uk_table_and_a_us_store_does_not()
    {
        var page = $"<html><body><div class=\"description\">{VeloforteNutrition}{VeloforteAminoAcids}</div></body></html>";
        var uk = new ShopifyStore("Veloforte", "https://veloforte.com", NutritionOnPage: true, Market: SiteMarket.Uk);
        var us = new ShopifyStore("Quest Nutrition", "https://www.questnutrition.com", NutritionOnPage: true);

        var ukDetails = await new ShopifyStoreScraper(new HttpClient(new PageHandler(page)), uk, NullLogger<ShopifyStoreScraper>.Instance)
            .FetchDetailsAsync("https://veloforte.com/products/collagen-pro");
        var usDetails = await new ShopifyStoreScraper(new HttpClient(new PageHandler(page)), us, NullLogger<ShopifyStoreScraper>.Instance)
            .FetchDetailsAsync("https://www.questnutrition.com/products/x");

        Assert.Equal(9.1m, ukDetails.ProteinPerServingGrams);
        Assert.Equal(10m, ukDetails.ServingSizeGrams);
        Assert.Equal("9.1g", JsonSerializer.Deserialize<Dictionary<string, string>>(ukDetails.NutritionJson!)!["Protein"]);
        Assert.Null(usDetails.NutritionJson);
    }

    [Fact]
    public async Task Bulk_reads_its_product_page_and_only_in_the_uk()
    {
        var bulk = MagentoStores.All.Single(s => s.BrandName == "Bulk");
        var handler = new PageHandler($"<html><body>{BulkBasicWhey}</body></html>");
        var scraper = new MagentoStoreScraper(new HttpClient(handler), bulk, NullLogger<MagentoStoreScraper>.Instance);

        var details = await scraper.FetchDetailsAsync("https://www.bulk.com/uk/products/basic-whey-protein/bpb-vwhe?o=MTc5LTE5MjU1");

        Assert.True(scraper.HasProductDetails);
        Assert.Equal(18m, details.ProteinPerServingGrams);
        Assert.Equal(30m, details.ServingSizeGrams);
        var usStore = bulk with { Market = SiteMarket.Us };
        Assert.False(new MagentoStoreScraper(new HttpClient(handler), usStore, NullLogger<MagentoStoreScraper>.Instance).HasProductDetails);
    }

    private sealed class PageHandler(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html"),
                RequestMessage = request,
            });
    }
}
