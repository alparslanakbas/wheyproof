using IndirimTakip.Infrastructure.NutritionLabels;

namespace IndirimTakip.Infrastructure.Tests;

public class PageNutritionTextTests
{
    // Shaped like a Quest product page (2026-09-14): a script mentioning the
    // panel, the description's "See Nutrition Facts" line, then the text summary.
    private const string QuestPage = """
        <html><body>
        <script>window.meta = {"body":"Nutrition Facts Calories 999 Protein 99g"};</script>
        <p>Quest is high quality protein. *Per serving. See Nutrition Facts for Calories Content. 7g of Total Fat.</p>
        <div class="facts">Nutrition Facts. Serving size: 1 bar (60g). Amount per serving: Calories: 180.
        Total Fat: 7g 9% Daily Value*. Saturated Fat 4g. Sodium: 220mg. Total Carbohydrate: 23g. 8% Daily Value*.
        Dietary Fiber: 12g. Total Sugars: 1g. Erythritol: 6g. Protein: 21g.</div>
        <table><tr><td>Nutrition Facts</td></tr><tr><td>Calories</td><td>180</td></tr></table>
        </body></html>
        """;

    [Fact]
    public void Reads_the_panel_that_passes_the_calorie_check()
    {
        var reading = PageNutritionText.Read(QuestPage);

        Assert.NotNull(reading);
        Assert.Equal(60m, reading.ServingSizeGrams);
        Assert.Equal(180m, reading.Calories);
        Assert.Equal(7m, reading.FatGrams);
        Assert.Equal(23m, reading.CarbohydrateGrams);
        Assert.Equal(12m, reading.FiberGrams);
        Assert.Equal(21m, reading.ProteinGrams);
    }

    // The script's "Calories 999 Protein 99g" must never be read.
    [Fact]
    public void Script_content_is_ignored() =>
        Assert.NotEqual(999m, PageNutritionText.Read(QuestPage)?.Calories);

    // Table cells on their own lines: "180" and the "% Daily Value" cell
    // under it must stay apart.
    [Fact]
    public void Table_cells_keep_their_own_lines()
    {
        var reading = PageNutritionText.Read("""
            <table>
            <tr><th>Nutrition Facts</th></tr>
            <tr><td>Serving size</td><td>1 scoop (31g)</td></tr>
            <tr><td>Calories</td><td>110</td></tr>
            <tr><td>% Daily Value*</td></tr>
            <tr><td>Total Fat</td><td>0g</td><td>0%</td></tr>
            <tr><td>Total Carbohydrate</td><td>3g</td><td>1%</td></tr>
            <tr><td>Protein</td><td>24g</td></tr>
            </table>
            """);

        Assert.NotNull(reading);
        Assert.Equal(110m, reading.Calories);
        Assert.Equal(24m, reading.ProteinGrams);
    }

    [Fact]
    public void A_page_without_a_checkable_panel_gives_nothing() =>
        Assert.Null(PageNutritionText.Read("<p>See Nutrition Facts for Calories Content.</p>"));
}
