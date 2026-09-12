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
/// <b>FRESHNESS.</b> The reference price is the high of a SLIDING 30-day window,
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

    public async Task<int> RefreshAsync(CancellationToken cancellationToken = default)
    {
        // One statement, set-based. NO per-product loop; that would be the very
        // problem we are fixing.
        //
        // `latest`       : the newest price point (one row per product via DISTINCT ON)
        // `price_window` : highest/lowest price of the last 30 days
        //
        // Products with NO price history keep NULL fields; queries already leave
        // those products out (stale/no data).
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
            )
            UPDATE "Products" p
            SET "LatestPrice"           = latest."Price",
                "LatestStoreOldPrice"   = latest."StoreOldPrice",
                "LatestScrapedAt"       = latest."ScrapedAt",
                "ReferencePrice30"      = price_window.highest,
                "LowestPrice30"         = price_window.lowest,
                "PriceSummaryUpdatedAt" = @now
            FROM latest
            LEFT JOIN price_window ON price_window."ProductId" = latest."ProductId"
            WHERE p."Id" = latest."ProductId"
              AND (
                    p."LatestPrice"      IS DISTINCT FROM latest."Price"
                 OR p."LatestStoreOldPrice" IS DISTINCT FROM latest."StoreOldPrice"
                 OR p."LatestScrapedAt" IS DISTINCT FROM latest."ScrapedAt"
                 OR p."ReferencePrice30" IS DISTINCT FROM price_window.highest
                 OR p."LowestPrice30"   IS DISTINCT FROM price_window.lowest
              );
            """;

        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddDays(-WindowDays);

        var affected = await db.Database.ExecuteSqlRawAsync(
            sql,
            [
                new Npgsql.NpgsqlParameter("windowStart", windowStart),
                new Npgsql.NpgsqlParameter("now", now),
            ],
            cancellationToken);

        logger.LogInformation("Price summary updated: {Count} products changed.", affected);
        return affected;
    }
}
