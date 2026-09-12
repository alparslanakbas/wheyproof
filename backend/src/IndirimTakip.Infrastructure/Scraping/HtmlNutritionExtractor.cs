using HtmlAgilityPack;

namespace IndirimTakip.Infrastructure.Scraping;

// Many stores give nutrition in a classic <table>. This is the shared place that
// turns rows into raw (label, value) pairs; normalizing and filtering happen in
// NutritionParser.
internal static class HtmlNutritionExtractor
{
    public static IEnumerable<(string Label, string Value)> FromTables(HtmlNode container)
    {
        // "self::table" is included so it also works when the caller passes the
        // <table> node itself rather than a container (e.g. a scoped
        // nutrition-table element).
        var tables = container.SelectNodes(".//table | self::table");
        if (tables is null)
            yield break;

        foreach (var table in tables)
        {
            foreach (var row in table.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
            {
                var cells = row.SelectNodes("./td|./th");
                if (cells is null || cells.Count < 2)
                    continue;

                // A header row ("Nutrient | per 100 g | per 4 g") isn't data, it labels
                // the columns; it's recognized because its cells are <th>.
                if (cells[0].Name == "th")
                    continue;

                var label = HtmlEntity.DeEntitize(cells[0].InnerText).Trim();
                // Some stores use a 3-column table ("Nutrient | per 100 g | per
                // serving"): the middle value is per 100 g, the LAST column is the
                // real per-serving value. With 2 columns there is one value anyway.
                var value = HtmlEntity.DeEntitize(cells[^1].InnerText).Trim();

                if (label.Length > 0 && value.Length > 0)
                    yield return (label, value);
            }
        }
    }

    /// <summary>
    /// For sources that give nutrition in div rows instead of a
    /// <c>&lt;table&gt;</c>: in each row the FIRST child element carries the label
    /// and the LAST one the value.
    /// </summary>
    /// <remarks>
    /// <b>Why it was needed.</b> Measured on the Turkish site: only 6.7% of products
    /// had nutrition, and part of the gap wasn't "the source has no data" but "the
    /// source doesn't use a table". One store's product page published complete
    /// nutrition (energy, fat, carbohydrate, protein...) without a single
    /// <c>&lt;table&gt;</c>, all as label/value pairs in div rows.
    /// <see cref="FromTables"/> couldn't see it.
    ///
    /// The row selector comes FROM THE CALLER and isn't guessed here: a generic rule
    /// such as "every div with two children" would treat every layout row on the page
    /// as a nutrition row.
    /// </remarks>
    public static IEnumerable<(string Label, string Value)> FromRowElements(HtmlNode container, string rowXPath)
    {
        foreach (var row in container.SelectNodes(rowXPath) ?? Enumerable.Empty<HtmlNode>())
        {
            var cells = row.SelectNodes("./*");
            if (cells is null || cells.Count < 2)
                continue;

            var label = HtmlEntity.DeEntitize(cells[0].InnerText).Trim();
            var value = HtmlEntity.DeEntitize(cells[^1].InnerText).Trim();

            if (label.Length > 0 && value.Length > 0)
                yield return (label, value);
        }
    }

    /// <summary>
    /// Multi-column nutrition tables: picks the right column while skipping "%DV"
    /// columns, and splits several nutrients squeezed into one cell with
    /// <c>&lt;br&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <b>WHY <see cref="FromTables"/> ISN'T ENOUGH.</b> That method takes the LAST
    /// column, which is right when the last column is the per-serving value. One
    /// store's columns were:
    /// <c>NUTRIENT | PER 100g | PER 100g %RI | PER 30g | PER 30g %RI</c>
    /// so the last column is a PERCENTAGE. Taking it would silently write a wrong
    /// value such as "Protein: 6"; being a number, no filter would catch it.
    /// Rule: the rightmost column whose header has NO "%" (the narrowest serving);
    /// if every header has one, the last column.
    ///
    /// <b>SECOND TRAP: rows packed with br.</b> A source can put two nutrients in one
    /// row: label <c>FAT&lt;br&gt;SATURATED FAT</c>, value <c>1.32g&lt;br&gt;0.81g</c>.
    /// Reading plain text would produce "FAT SATURATED FAT = 1.32g 0.81g", which also
    /// passes the filter (the value has a number) and enters the table as a nonsense
    /// row. If the part counts match the row is split; if not it is NOT split (better
    /// joined than mismatched).
    ///
    /// <b>THE HEADER ROW ISN'T FIXED.</b> It may not be <c>&lt;th&gt;</c> (one store
    /// uses <c>&lt;td&gt;&lt;strong&gt;</c>) and it may not be the FIRST ROW (another
    /// store's table starts with a single-cell product title). The header is the first
    /// row with at least two cells; rows before it are skipped.
    /// </remarks>
    public static IEnumerable<(string Label, string Value)> FromMultiColumnTable(HtmlNode container)
    {
        var tables = container.SelectNodes(".//table | self::table");
        if (tables is null)
            yield break;

        foreach (var table in tables)
        {
            var rows = table.SelectNodes(".//tr");
            if (rows is null || rows.Count < 2)
                continue;

            // The header is NOT ALWAYS the first row: one store's table starts with a
            // single-cell product title. Looking only at the first row and skipping
            // the table meant losing that table entirely; measured, the macro table
            // dropped out and only an enzyme table with "**" values remained.
            var headerIndex = rows
                .Select((row, index) => (row, index))
                .FirstOrDefault(x => (x.row.SelectNodes("./td|./th")?.Count ?? 0) >= 2)
                .index;

            var headerCells = rows[headerIndex].SelectNodes("./td|./th");
            if (headerCells is null || headerCells.Count < 2)
                continue;

            var targetIndex = PickValueColumn(headerCells);

            foreach (var row in rows.Skip(headerIndex + 1))
            {
                var cells = row.SelectNodes("./td|./th");
                if (cells is null || cells.Count <= targetIndex)
                    continue;

                var labels = SplitOnLineBreaks(cells[0]);
                var values = SplitOnLineBreaks(cells[targetIndex]);

                // If the part counts match, one row per nutrient; otherwise one
                // joined row (don't pair them up wrongly).
                if (labels.Count == values.Count && labels.Count > 1)
                {
                    for (var i = 0; i < labels.Count; i++)
                    {
                        if (labels[i].Length > 0 && values[i].Length > 0)
                            yield return (labels[i], values[i]);
                    }
                    continue;
                }

                var label = string.Join(" ", labels).Trim();
                var value = string.Join(" ", values).Trim();
                if (label.Length > 0 && value.Length > 0)
                    yield return (label, value);
            }
        }
    }

    /// <summary>
    /// Header of the column <see cref="FromMultiColumnTable"/> PICKS.
    /// </summary>
    /// <remarks>
    /// Some sources write the serving size not in a separate field but in exactly
    /// this header ("PER 30g"). It's exposed here so the column rule isn't written a
    /// second time in a scraper; two copies would drift apart and the serving would
    /// start being read from the wrong column.
    /// </remarks>
    public static string? MultiColumnPortionHeader(HtmlNode container)
    {
        foreach (var table in container.SelectNodes(".//table | self::table") ?? Enumerable.Empty<HtmlNode>())
        {
            // The header row may not be the first row (see FromMultiColumnTable).
            var headerCells = table.SelectNodes(".//tr")
                ?.Select(row => row.SelectNodes("./td|./th"))
                .FirstOrDefault(cells => (cells?.Count ?? 0) >= 2);
            if (headerCells is null || headerCells.Count < 2)
                continue;

            return HtmlEntity.DeEntitize(headerCells[PickValueColumn(headerCells)].InnerText)?.Trim();
        }

        return null;
    }

    /// <summary>The rightmost column whose header has no "%"; otherwise the last column.</summary>
    private static int PickValueColumn(HtmlNodeCollection headerCells)
    {
        for (var i = headerCells.Count - 1; i >= 1; i--)
        {
            var header = HtmlEntity.DeEntitize(headerCells[i].InnerText) ?? string.Empty;
            if (!header.Contains('%'))
                return i;
        }

        return headerCells.Count - 1;
    }

    /// <summary>Splits a cell into parts at &lt;br&gt; boundaries.</summary>
    private static List<string> SplitOnLineBreaks(HtmlNode cell)
    {
        var parts = new List<string>();
        var buffer = new System.Text.StringBuilder();

        void Flush()
        {
            var text = HtmlEntity.DeEntitize(buffer.ToString()).Trim();
            if (text.Length > 0)
                parts.Add(text);
            buffer.Clear();
        }

        foreach (var node in cell.DescendantsAndSelf())
        {
            if (node.Name == "br")
                Flush();
            else if (node.NodeType == HtmlNodeType.Text)
                buffer.Append(node.InnerText);
        }

        Flush();
        return parts;
    }

    // Some sources give nutrition not as an HTML <table> but inside one description
    // paragraph as "<strong>Label</strong> — value<br>" lines (confirmed on a real
    // product page). The <strong> text before "—" is the label; the text after it, up
    // to the next <strong>/<br>, is the value.
    public static IEnumerable<(string Label, string Value)> FromLabelDashValuePattern(HtmlNode container)
    {
        foreach (var strong in container.SelectNodes(".//strong") ?? Enumerable.Empty<HtmlNode>())
        {
            // Also trims the leading dash of sub-item labels such as "— Sugars".
            var label = HtmlEntity.DeEntitize(strong.InnerText).TrimStart(' ', '—', '-').Trim();
            if (label.Length == 0)
                continue;

            // Collects the text after the <strong> tag on the same line (up to the
            // next <br>/<strong>).
            var value = new System.Text.StringBuilder();
            for (var sibling = strong.NextSibling; sibling is not null; sibling = sibling.NextSibling)
            {
                if (sibling.Name is "br" or "strong")
                    break;
                value.Append(HtmlEntity.DeEntitize(sibling.InnerText ?? sibling.OuterHtml));
            }

            var valueText = value.ToString().TrimStart(' ', '—', '-', ':').Trim();
            if (valueText.Length > 0)
                yield return (label, valueText);
        }
    }
}
