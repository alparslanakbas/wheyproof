using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Deals;

/// <summary>
/// Recomputes the price summary fields on <c>Products</c>.
///
/// <b>WHY IT EXISTS (measured on the Turkish site).</b> 97.7% of a
/// <c>/api/deals</c> request was spent inside PostgreSQL: COUNT 654 ms + data
/// query 1,437 ms, 49 ms in C#. The cause: the query ran 6-8 correlated subqueries
/// over <c>PriceHistories</c> for EVERY one of 2,713 products (last price, 30-day
/// high/low, and the same subqueries again for the discount percentage). The
/// (<c>ProductId, ScrapedAt</c>) index already existed; the problem wasn't the
/// cost of one lookup but repeating it 2,713 times.
///
/// Here the same information is computed with ONE set-based query and written to
/// the product.
///
/// <b>DERIVED DATA.</b> These fields aren't a source; <c>PriceHistories</c> stays
/// the single source of truth. If the columns were dropped they could be
/// recomputed; if wrong, they can be corrected.
///
/// <b>THE WINDOW IS FIXED AT 30 DAYS.</b> If <c>GetDealsAsync</c> gets a
/// <c>days</c> other than 30, the query falls back to the old live calculation;
/// that path stays on purpose.
///
/// <b>REFERENCE PRICE = USUAL PRICE (2026-10-03).</b> The highest price seen on
/// at least <see cref="UsualPriceDays"/> DIFFERENT days in the window; if the
/// current price is above it (a price rise) or there is none (a new product),
/// the current price, so the discount is 0. It used to be the plain high, and a
/// price seen in a single scrape made a 30-day "verified discount": measured
/// on the UK site, 157 of 162 discounts rested on a price seen for less than a
/// week (the Turkish site's homepage led with a "70.1%" discount that was a
/// price rise after a few days' spike). The threshold is the owner's decision
/// ("a week's price"). The column keeps its name and changes meaning on
/// purpose: list queries drop products with a NULL reference, so a new column
/// would have dropped products with short histories. The lists' discount
/// filter, percentage and sorting already read this column; the product page,
/// favorites, the preferred band and the homepage/brand stats computed the
/// reference live (the window's high) and all read this column now too,
/// falling back to the live calculation while it's still empty (a new product).
///
/// <b>FRESHNESS.</b> The reference price comes from a SLIDING 30-day window,
/// so it changes even without a new scrape once an old point leaves the window.
/// That's why it is recomputed after every scrape. The drift is at most one
/// scrape round and can only come from a point 30 days old dropping out.
/// </summary>
public sealed class PriceSummaryRefresher(AppDbContext db, ILogger<PriceSummaryRefresher> logger)
{
    /// <summary>
    /// The window the summary covers. <c>GetDealsAsync</c> can use the precomputed
    /// fields only for a <c>days</c> equal to this value.
    /// </summary>
    public const int WindowDays = 30;

    /// <summary>
    /// The least number of different days (UTC) a price must be seen in the window
    /// to count as usual. The owner's decision: a week.
    /// </summary>
    public const int UsualPriceDays = 7;

    public async Task<int> RefreshAsync(CancellationToken cancellationToken = default)
    {
        // One statement, set-based. NO per-product loop; that would be the very
        // problem we are fixing.
        //
        // `latest`       : the newest price point (one row per product via DISTINCT ON)
        // `price_window` : highest/lowest price of the last 30 days
        // `daily`        : on how many different days each price was seen in the window
        // `usual`        : the highest price seen on at least UsualPriceDays days
        //
        // A product with an empty window (not scraped for 30 days) keeps a NULL
        // reference as before, and products with NO price history keep NULL
        // fields; queries already leave those products out (stale/no data).
        const string sql = """
            WITH latest AS (
                SELECT DISTINCT ON (ph."ProductId")
                       ph."ProductId", ph."Price", ph."StoreOldPrice", ph."ScrapedAt"
                FROM "PriceHistories" ph
                ORDER BY ph."ProductId", ph."ScrapedAt" DESC
            ),
            price_window AS (
                SELECT ph."ProductId",
                       MAX(ph."Price") AS highest,
                       MIN(ph."Price") AS lowest
                FROM "PriceHistories" ph
                WHERE ph."ScrapedAt" >= @windowStart
                GROUP BY ph."ProductId"
            ),
            daily AS (
                SELECT ph."ProductId", ph."Price",
                       COUNT(DISTINCT (ph."ScrapedAt" AT TIME ZONE 'UTC')::date) AS days
                FROM "PriceHistories" ph
                WHERE ph."ScrapedAt" >= @windowStart
                GROUP BY ph."ProductId", ph."Price"
            ),
            usual AS (
                SELECT d."ProductId", MAX(d."Price") AS price
                FROM daily d
                WHERE d.days >= @usualDays
                GROUP BY d."ProductId"
            ),
            fresh AS (
                SELECT latest."ProductId", latest."Price", latest."StoreOldPrice", latest."ScrapedAt",
                       price_window.lowest,
                       -- GREATEST skips NULL: with no usual price, the current one.
                       CASE WHEN price_window."ProductId" IS NULL THEN NULL
                            ELSE GREATEST(usual.price, latest."Price") END AS reference
                FROM latest
                LEFT JOIN price_window ON price_window."ProductId" = latest."ProductId"
                LEFT JOIN usual ON usual."ProductId" = latest."ProductId"
            )
            UPDATE "Products" p
            SET "LatestPrice"           = fresh."Price",
                "LatestStoreOldPrice"   = fresh."StoreOldPrice",
                "LatestScrapedAt"       = fresh."ScrapedAt",
                "ReferencePrice30"      = fresh.reference,
                "LowestPrice30"         = fresh.lowest,
                "PriceSummaryUpdatedAt" = @now
            FROM fresh
            WHERE p."Id" = fresh."ProductId"
              AND (
                    p."LatestPrice"      IS DISTINCT FROM fresh."Price"
                 OR p."LatestStoreOldPrice" IS DISTINCT FROM fresh."StoreOldPrice"
                 OR p."LatestScrapedAt" IS DISTINCT FROM fresh."ScrapedAt"
                 OR p."ReferencePrice30" IS DISTINCT FROM fresh.reference
                 OR p."LowestPrice30"   IS DISTINCT FROM fresh.lowest
              );
            """;

        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddDays(-WindowDays);

        var affected = await db.Database.ExecuteSqlRawAsync(
            sql,
            [
                new Npgsql.NpgsqlParameter("windowStart", windowStart),
                new Npgsql.NpgsqlParameter("usualDays", UsualPriceDays),
                new Npgsql.NpgsqlParameter("now", now),
            ],
            cancellationToken);

        logger.LogInformation("Price summary updated: {Count} products changed.", affected);
        return affected;
    }
}
