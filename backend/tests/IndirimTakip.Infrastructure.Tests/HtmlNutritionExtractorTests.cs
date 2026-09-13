using System.Text.Json;
using HtmlAgilityPack;
using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

// These tests use raw HTML fragments taken from real product pages of three Turkish
// stores. All three structures turned out different from what was first assumed (a
// 3-column table whose last column is per serving; one div per row; no <table> at all
// but a <strong>label</strong> — value<br> pattern). The snapshots keep the same
// mistake (reading the wrong column or structure) from coming back unnoticed.
public class HtmlNutritionExtractorTests
{
    [Fact]
    public void Table_takes_the_last_column_as_the_per_serving_value()
    {
        // From a real product: headers "Component | 100 g | 4 g", the last column (a
        // 4 g serving) is the real per-serving value.
        const string html = """
            <table class="nutrition-table">
            <tbody>
            <tr><th>Bileşen</th><th>100 g</th><th>4 g</th></tr>
            <tr><td>Enerji</td><td>102 kJ / 24 kcal</td><td>8 kJ / 1 kcal</td></tr>
            <tr><td>Protein</td><td>0 g</td><td>0 g</td></tr>
            <tr><td>Kreatin Monohidrat 500 Mesh (CREA500®)</td><td>75 g</td><td>3 g</td></tr>
            </tbody>
            </table>
            """;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var table = doc.DocumentNode.SelectSingleNode("//table");
        var json = NutritionParser.BuildNutritionJson(HtmlNutritionExtractor.FromTables(table));
        var parsed = Deserialize(json);

        // The real serving value ("3 g") must be captured, not the per-100 g one ("75 g").
        Assert.Equal("3 g", parsed["Kreatin Monohidrat 500 Mesh (CREA500®)"]);
        // The header row must not leak in as a data row.
        Assert.False(parsed.ContainsKey("Bileşen"));
    }

    [Fact]
    public void Div_rows_are_read_one_by_one()
    {
        // From a real creatine product page.
        const string html = """
            <div class="satirlar"><span class="baslik">Enerji/Calorie</span> <span class="deger">0 kcal (0 kj)</span></div>
            <div class="satirlar"><span class="baslik">Protein/Protein</span> <span class="deger">0 g</span></div>
            <div class="satirlar"><span class="baslik">Kreatin Monohidrat/Creatine Monohydrate</span> <span class="deger">5 g</span></div>
            """;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var rows = doc.DocumentNode
            .SelectNodes("//div[contains(concat(' ', normalize-space(@class), ' '), ' satirlar ')]")!
            .Select(row =>
            {
                var label = row.SelectSingleNode(".//span[contains(concat(' ', normalize-space(@class), ' '), ' baslik ')]");
                var value = row.SelectSingleNode(".//span[contains(concat(' ', normalize-space(@class), ' '), ' deger ')]");
                return (Label: label!.InnerText.Trim(), Value: value!.InnerText.Trim());
            });

        var json = NutritionParser.BuildNutritionJson(rows);
        var parsed = Deserialize(json);

        // An EXACT repeat such as "Protein/Protein" is simplified, but "Kreatin
        // Monohidrat/Creatine Monohydrate" is a real translation (not identical) and
        // must not be; NormalizeLabel deliberately merges only two identical halves.
        Assert.Equal("5 g", parsed["Kreatin Monohidrat/Creatine Monohydrate"]);
        Assert.Equal(22m, NutritionParser.ExtractProteinGrams(
            NutritionParser.BuildNutritionJson([("Protein/Protein", "22 g")])));
    }

    [Fact]
    public void Label_dash_value_pattern_is_read_without_a_table()
    {
        // From a real creatine product page: no <table> at all,
        // <strong>label</strong> — value<br> one after another.
        const string html = """
            <p><strong>Enerji</strong> — 0 kj / 0 kcal<br>
            <strong>Protein</strong> — 0 g<br>
            <strong>— Şekerler</strong> — 0 g<br>
            <strong>Kreatin Monohidrat</strong> — 5 g</p>
            """;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var rows = HtmlNutritionExtractor.FromLabelDashValuePattern(doc.DocumentNode);
        var json = NutritionParser.BuildNutritionJson(rows);
        var parsed = Deserialize(json);

        Assert.Equal("5 g", parsed["Kreatin Monohidrat"]);
        // A sub-item's leading dash must be trimmed from both label and value.
        Assert.Equal("0 g", parsed["Şekerler"]);
        Assert.False(parsed.ContainsKey("— Şekerler"));
    }

    private static Dictionary<string, string> Deserialize(string? json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json!)!;
}
