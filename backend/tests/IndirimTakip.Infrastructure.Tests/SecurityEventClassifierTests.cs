using IndirimTakip.Infrastructure.Security;

namespace IndirimTakip.Infrastructure.Tests;

// This class can go wrong in two directions and both are silent: too broad and it
// records normal traffic (volume + needless personal data), too narrow and it
// misses a real attack. Each is tested separately.
public class SecurityEventClassifierTests
{
    [Theory]
    [InlineData(429, "rate-limited")]
    [InlineData(401, "unauthorized")]
    [InlineData(403, "unauthorized")]
    [InlineData(500, "server-error")]
    [InlineData(502, "server-error")]
    [InlineData(503, "server-error")]
    public void Noteworthy_status_codes_are_recorded(int status, string expected)
    {
        Assert.Equal(expected, SecurityEventClassifier.Classify(status, "/api/dev/click-report"));
    }

    // THE MOST IMPORTANT TEST: most 404s are innocent (deleted product, old link).
    // Recording them all would fill the table with noise and make the panel unreadable.
    [Theory]
    [InlineData("/product/4304/optimum-nutrition-gold-standard-whey-5-lb")]
    [InlineData("/brand/ghost/protein-powder")]
    [InlineData("/category/creatine")]
    [InlineData("/")]
    public void Innocent_404_is_not_recorded(string path)
    {
        Assert.Null(SecurityEventClassifier.Classify(404, path));
    }

    [Theory]
    [InlineData("/wp-admin/setup-config.php")]
    [InlineData("/.env")]
    [InlineData("/vendor/phpunit/phpunit/src/Util/PHP/eval-stdin.php")]
    [InlineData("/.git/config")]
    [InlineData("/phpmyadmin/index.php")]
    [InlineData("/xmlrpc.php")]
    public void Known_exploit_scan_is_caught(string path)
    {
        Assert.Equal("probe", SecurityEventClassifier.Classify(404, path));
    }

    // Successful requests are NEVER recorded; page views aren't what this table is for.
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(301)]
    [InlineData(304)]
    public void Normal_request_is_not_recorded(int status)
    {
        Assert.Null(SecurityEventClassifier.Classify(status, "/api/deals"));
    }

    // CULTURE TRAP: the comparison uses the invariant culture, and THAT IS RIGHT.
    // Lowercased with a Turkish culture, "I" would become a dotless "ı" and a
    // scan for an ".INI" file would silently slip through.
    [Fact]
    public void Uppercase_extension_is_not_tripped_by_culture()
    {
        Assert.True(SecurityEventClassifier.LooksLikeProbe("/CONFIG.INI"));
        Assert.True(SecurityEventClassifier.LooksLikeProbe("/WP-ADMIN/INDEX.PHP"));
    }

    // The other direction: non-ASCII letters in a path must not crash or match
    // wrongly. Product names end up in slugs, so such paths really occur.
    [Fact]
    public void Path_with_non_ascii_letters_does_not_match_wrongly()
    {
        Assert.False(SecurityEventClassifier.LooksLikeProbe("/product/12/CAFÉ-PROTEİN-BLEND"));
        Assert.Null(SecurityEventClassifier.Classify(404, "/brand/Crème/vitamins"));
    }

    [Fact]
    public void Empty_path_does_not_crash()
    {
        Assert.False(SecurityEventClassifier.LooksLikeProbe(""));
        Assert.Null(SecurityEventClassifier.Classify(404, ""));
    }
}
