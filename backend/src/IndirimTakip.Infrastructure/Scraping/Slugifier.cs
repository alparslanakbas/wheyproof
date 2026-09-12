using System.Text;

namespace IndirimTakip.Infrastructure.Scraping;

// Builds a URL segment from a product name. **It MUST give exactly the same result
// as the frontend's `core/slugify.ts`**: if the URL it builds doesn't match the
// canonical URL, the link we report to search engines lands on a redirect and the
// notification loses its value.
//
// Turkish letters are mapped by hand; culture-dependent lowercasing isn't trusted.
// The Turkish site hit this trap three times: with tr-TR an uppercase "I" becomes a
// dotless "ı", with ToLowerInvariant an uppercase "İ" isn't lowercased at all, and
// in JavaScript an uppercase "İ" doesn't match a lowercase "i".
public static class Slugifier
{
    // So the URL segment doesn't balloon for long combination products.
    private const int MaxSlugLength = 80;

    public static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var mapped = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            mapped.Append(ch switch
            {
                'ç' or 'Ç' => 'c',
                'ğ' or 'Ğ' => 'g',
                'ı' or 'İ' => 'i',
                'ö' or 'Ö' => 'o',
                'ş' or 'Ş' => 's',
                'ü' or 'Ü' => 'u',
                _ => ch,
            });
        }

        // From here on only plain ASCII letters matter, so culture-independent
        // lowercasing is safe.
        var lowered = mapped.ToString().ToLowerInvariant();

        var slug = new StringBuilder(lowered.Length);
        var lastWasHyphen = true; // also prevents leading hyphens
        foreach (var ch in lowered)
        {
            if (ch is >= 'a' and <= 'z' || ch is >= '0' and <= '9')
            {
                slug.Append(ch);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                slug.Append('-');
                lastWasHyphen = true;
            }
        }

        var result = slug.ToString().Trim('-');
        if (result.Length <= MaxSlugLength) return result;

        var truncated = result[..MaxSlugLength];
        var lastHyphen = truncated.LastIndexOf('-');
        return lastHyphen > 0 ? truncated[..lastHyphen] : truncated;
    }
}
