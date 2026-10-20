using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IndirimTakip.Infrastructure.Deals;

/// <summary>A product that is really cheaper by the season-page rule.</summary>
public sealed record CampaignDiscount(DealDto Product, decimal PriorLowest, decimal RealPercent, DateTimeOffset DiscountStart);

/// <summary>Products the store shows with a crossed-out "was" price, classified by the season-page rule.</summary>
public sealed record CampaignSummary(
    DateTimeOffset MeasuredAt,
    int StoreSaysSale,
    int Real,
    int NotCheaper,
    int Permanent,
    int InsufficientData,
    IReadOnlyList<CampaignDiscount> RealDiscounts);

/// <summary>
/// Classifies the products a store shows with a crossed-out "was" price by one rule: today's price against the
/// lowest price in the 30 days BEFORE the discount started. Mirror of the Turkish site's KampanyaIndirimServisi
/// (2026-10-07), where that rule is the law (the Turkish price-label regulation, like the EU's Omnibus rule).
/// </summary>
/// <remarks>
/// <b>In the US and UK this is OUR rule, not a legal standard</b>, and the page says so: neither market fixes the
/// reference price as a 30-day low. It is stricter than the site's "real discount" label (today's price against
/// the usual price: the highest price seen on at least 7 days in 30); on the Turkish data the label found 127 of
/// 720 products discounted, this rule 32.
///
/// <b>Rules, unchanged from the Turkish site where they were measured:</b>
/// - The discount starts at the first observation of the uninterrupted run of today's price.
/// - Reference price: the lowest price seen on AT LEAST TWO DIFFERENT DAYS in the 30 days before that start. A
///   one-off low reading is usually a scraping error.
/// - Enough data: observations on at least 7 different days in those 30 days.
/// - Today's price unchanged for over 30 days: "permanent", the crossed-out price has not been a sale for a month.
/// - Real: today's price at least 0.5% below the reference.
///
/// A large gap is NOT treated as a data error: on the Turkish site a real 8-day store sale (prices 53-60% lower,
/// then back) looked like one and had been excluded by mistake.
///
/// First measurement here (2026-10-20): US 610 products with a "was" price, 49 real, 227 permanent, 133 not
/// cheaper, 201 short of data; UK 1,078, 11 real, 327 permanent, 354 not cheaper, 386 short of data.
/// </remarks>
public sealed class CampaignDiscountService(AppDbContext db, DealsQueryService deals)
{
    public const int WindowDays = 30;
    public const int MinimumDataDays = 7;
    public const int LowestSeenOnDays = 2;
    public const decimal RealThreshold = 0.995m;
    private static readonly TimeSpan FreshFor = TimeSpan.FromHours(48);

    private const string Query = """
        WITH fresh AS (
            SELECT p."Id", p."LatestPrice" AS price
            FROM "Products" p JOIN "Brands" b ON b."Id" = p."BrandId"
            WHERE p."IsActive" AND b."IsActive" AND p."LatestScrapedAt" > now() - @fresh
              AND p."LatestPrice" > 0 AND p."LatestStoreOldPrice" > p."LatestPrice"
        ),
        last_different AS (
            SELECT f."Id", max(ph."ScrapedAt") AS seen_at
            FROM fresh f JOIN "PriceHistories" ph ON ph."ProductId" = f."Id" AND ph."Price" <> f.price
            GROUP BY f."Id"
        ),
        run AS (
            SELECT f."Id", f.price,
                   (SELECT min(ph."ScrapedAt") FROM "PriceHistories" ph
                    WHERE ph."ProductId" = f."Id" AND ph."ScrapedAt" > coalesce(d.seen_at, '-infinity')) AS run_start
            FROM fresh f LEFT JOIN last_different d ON d."Id" = f."Id"
        ),
        prior AS (
            SELECT r."Id", r.price, r.run_start,
                   (SELECT min(x.price) FROM (
                        SELECT ph."Price" AS price FROM "PriceHistories" ph
                        WHERE ph."ProductId" = r."Id" AND ph."ScrapedAt" >= r.run_start - @window AND ph."ScrapedAt" < r.run_start
                        GROUP BY ph."Price"
                        HAVING count(DISTINCT (ph."ScrapedAt" AT TIME ZONE 'UTC')::date) >= @lowestDays) x) AS prior_min,
                   (SELECT count(DISTINCT (ph."ScrapedAt" AT TIME ZONE 'UTC')::date) FROM "PriceHistories" ph
                    WHERE ph."ProductId" = r."Id" AND ph."ScrapedAt" >= r.run_start - @window AND ph."ScrapedAt" < r.run_start) AS prior_days
            FROM run r
        )
        SELECT o."Id" AS "ProductId", o.price AS "Price", o.prior_min AS "PriorLowest", o.run_start AS "RunStart",
               CASE
                   WHEN o.run_start <= now() - @window THEN 'permanent'
                   WHEN o.prior_days < @dataDays OR o.prior_min IS NULL THEN 'insufficient'
                   WHEN o.price < o.prior_min * @threshold THEN 'real'
                   ELSE 'not-cheaper'
               END AS "Class"
        FROM prior o
        """;

    public async Task<CampaignSummary> SummaryAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Database.SqlQueryRaw<CampaignRow>(
                Query,
                new NpgsqlParameter("fresh", FreshFor),
                new NpgsqlParameter("window", TimeSpan.FromDays(WindowDays)),
                new NpgsqlParameter("lowestDays", LowestSeenOnDays),
                new NpgsqlParameter("dataDays", MinimumDataDays),
                new NpgsqlParameter("threshold", RealThreshold))
            .ToListAsync(cancellationToken);

        var real = rows.Where(r => r.Class == "real" && r.PriorLowest > 0).ToList();
        var products = (await deals.GetDealsByIdsAsync(real.Select(r => r.ProductId).ToList(), cancellationToken: cancellationToken))
            .ToDictionary(d => d.ProductId);

        var list = real
            .Where(r => products.ContainsKey(r.ProductId))
            .Select(r => new CampaignDiscount(
                products[r.ProductId],
                r.PriorLowest!.Value,
                Math.Round(100 * (r.PriorLowest.Value - r.Price) / r.PriorLowest.Value, 1),
                r.RunStart))
            .OrderByDescending(d => d.RealPercent)
            .ToList();

        int Count(string cls) => rows.Count(r => r.Class == cls);
        return new CampaignSummary(
            DateTimeOffset.UtcNow, rows.Count, real.Count, Count("not-cheaper"), Count("permanent"), Count("insufficient"), list);
    }

    internal sealed class CampaignRow
    {
        public int ProductId { get; set; }
        public decimal Price { get; set; }
        public decimal? PriorLowest { get; set; }
        public DateTimeOffset RunStart { get; set; }
        public string Class { get; set; } = string.Empty;
    }
}
