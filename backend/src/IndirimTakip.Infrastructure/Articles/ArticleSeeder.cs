using System.Reflection;
using System.Text.RegularExpressions;
using IndirimTakip.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndirimTakip.Infrastructure.Articles;

/// <summary>
/// Guide articles shipped with the code (Articles/Content/*.html, embedded).
/// </summary>
/// <remarks>
/// <b>WHY IN THE REPO, NOT ONLY IN THE DATABASE.</b> Articles entered through
/// the admin API live only in one database: a fresh environment starts with
/// an empty guides page, and the frontend links to specific slugs
/// (category guides, calculators, learning paths) that would all 404.
///
/// <b>ONLY MISSING SLUGS ARE ADDED; EXISTING ARTICLES ARE NEVER TOUCHED.</b>
/// An article edited later through the admin API must not be overwritten on
/// the next startup. To change an already-published article, use the PUT
/// endpoint, not this file.
/// </remarks>
public static class ArticleSeeder
{
    private const string ResourcePrefix = "articles/";

    // <!-- title: ... / summary: ... / published: ... --> at the top of the file.
    private static readonly Regex Header = new(@"\A\s*<!--(?<meta>.*?)-->", RegexOptions.Singleline);

    public record SeedArticle(string Slug, string Title, string Summary, DateTimeOffset PublishedAt, string Body);

    /// <summary>Parses one content file. Throws on a missing or malformed header.</summary>
    public static SeedArticle Parse(string slug, string content)
    {
        var match = Header.Match(content);
        if (!match.Success)
            throw new FormatException($"Article '{slug}' has no metadata header.");

        var meta = match.Groups["meta"].Value
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains(':'))
            .ToDictionary(
                line => line[..line.IndexOf(':')].Trim().ToLowerInvariant(),
                line => line[(line.IndexOf(':') + 1)..].Trim());

        string Required(string key) =>
            meta.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new FormatException($"Article '{slug}' is missing '{key}'.");

        var body = content[(match.Index + match.Length)..].Trim();
        if (body.Length == 0)
            throw new FormatException($"Article '{slug}' has an empty body.");

        return new SeedArticle(
            slug,
            Required("title"),
            Required("summary"),
            DateTimeOffset.Parse(Required("published"), System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime(),
            body);
    }

    /// <summary>Every embedded article, parsed.</summary>
    public static IReadOnlyList<SeedArticle> LoadEmbedded()
    {
        var assembly = typeof(ArticleSeeder).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".html", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                var slug = name[ResourcePrefix.Length..^".html".Length];
                return Parse(slug, reader.ReadToEnd());
            })
            .ToList();
    }

    /// <summary>Adds embedded articles whose slug isn't in the database yet. Returns how many were added.</summary>
    public static int SeedMissing(AppDbContext db)
    {
        var existing = db.Articles.AsNoTracking().Select(a => a.Slug).ToHashSet(StringComparer.Ordinal);
        var added = 0;

        foreach (var article in LoadEmbedded().Where(a => !existing.Contains(a.Slug)))
        {
            db.Articles.Add(new Article
            {
                Slug = article.Slug,
                Title = article.Title,
                Summary = article.Summary,
                Body = article.Body,
                PublishedAt = article.PublishedAt,
                IsPublished = true,
            });
            added++;
        }

        if (added > 0)
            db.SaveChanges();

        return added;
    }
}
