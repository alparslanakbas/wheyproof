using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndirimTakip.Infrastructure.Coupons;

public record CreateCouponRequest(
    string? BrandName,
    string? Seller,
    // May be empty for promotions without a code; see Coupon.Code.
    string? Code,
    string Description,
    DateTimeOffset? ValidUntil)
{
    public bool HasExactlyOneTarget =>
        !string.IsNullOrWhiteSpace(BrandName) ^ !string.IsNullOrWhiteSpace(Seller);
}

// IsActive is included so an expired or wrong coupon can be deactivated
// through the API rather than directly in the database.
// WHY ClearValidUntil IS A SEPARATE FIELD: on this endpoint null means "leave
// it alone", not "clear it". That's fine for the code (an empty STRING clears
// it), but a date has no empty string, so there was no way to make a dated
// coupon open-ended again. An explicit flag beats a field that silently
// does nothing.
public record UpdateCouponRequest(
    string? Code,
    string? Description,
    DateTimeOffset? ValidUntil,
    bool? IsActive,
    bool? ClearValidUntil = null);

public class CouponService(AppDbContext db)
{
    public async Task<IReadOnlyList<CouponDto>> GetActiveCouponsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        return await db.Coupons
            .Where(c => c.IsActive && (c.ValidUntil == null || c.ValidUntil >= now))
            .OrderBy(c => c.Seller ?? c.Brand!.Name)
            .Select(c => new CouponDto(
                c.Id,
                c.Brand != null ? c.Brand.Name : null,
                c.Seller,
                c.Code,
                c.Description,
                c.ValidUntil,
                c.LastVerifiedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<CouponDto?> CreateAsync(CreateCouponRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.HasExactlyOneTarget)
            throw new ArgumentException("A coupon must belong to exactly one brand or one seller.", nameof(request));

        Brand? brand = null;
        if (!string.IsNullOrWhiteSpace(request.BrandName))
        {
            var wanted = request.BrandName.Trim();
            brand = await db.Brands.FirstOrDefaultAsync(b => b.Name == wanted, cancellationToken);

            if (brand is null)
            {
                // AN EXACT MATCH ISN'T ENOUGH. The brand name is typed BY HAND
                // and the catalog's spelling isn't always remembered ("DrPan"
                // vs "Dr Pan" returned a 404).
                //
                // The scraping side solved the same problem long ago:
                // FoldBrandName folds letters, drops spaces and dots and keeps
                // hyphens. It's reused rather than copied; two copies would
                // drift apart into a baffling "the scraper finds it, the coupon
                // doesn't".
                var folded = ScrapeIngestionService.FoldBrandName(wanted);
                brand = (await db.Brands.ToListAsync(cancellationToken))
                    .FirstOrDefault(b => ScrapeIngestionService.FoldBrandName(b.Name) == folded);
            }

            if (brand is null)
                return null;
        }

        var seller = string.IsNullOrWhiteSpace(request.Seller)
            ? null
            : request.Seller.Trim().ToLowerInvariant();

        var coupon = new Coupon
        {
            BrandId = brand?.Id,
            Seller = seller,
            // An empty string and NULL mean the same thing ("no code"); stored
            // one way so the UI doesn't check two kinds of empty.
            Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim(),
            Description = request.Description.Trim(),
            ValidUntil = ToUtc(request.ValidUntil),
            LastVerifiedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(coupon, brand?.Name);
    }

    public async Task<CouponDto?> UpdateAsync(int id, UpdateCouponRequest request, CancellationToken cancellationToken = default)
    {
        var coupon = await db.Coupons.Include(c => c.Brand).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (coupon is null)
            return null;

        if (request.Code is not null)
            coupon.Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
        if (request.Description is not null) coupon.Description = request.Description;
        if (request.ClearValidUntil == true)
            coupon.ValidUntil = null;
        else if (request.ValidUntil is not null)
            coupon.ValidUntil = ToUtc(request.ValidUntil);
        if (request.IsActive is not null) coupon.IsActive = request.IsActive.Value;

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(coupon, coupon.Brand?.Name);
    }

    /// <summary>Converts a date to UTC.</summary>
    /// <remarks>
    /// <b>REQUIRED.</b> Npgsql can only write a <see cref="DateTimeOffset"/>
    /// with offset 0 to a <c>timestamp with time zone</c> column; a date with
    /// a local offset (e.g. "-04:00") FAILS with
    /// <c>"only offset 0 (UTC) is supported"</c> and the request returns 500.
    ///
    /// The trap is sneaky because it depends on the sender: the browser's
    /// <c>&lt;input type="date"&gt;</c> sends no offset ("2026-12-31"), so the
    /// panel never shows the error, but calling the same endpoint from a
    /// script or another client blows up.
    /// </remarks>
    private static DateTimeOffset? ToUtc(DateTimeOffset? value) =>
        value?.ToUniversalTime();

    private static CouponDto ToDto(Coupon coupon, string? brandName) =>
        new(
            coupon.Id,
            brandName,
            coupon.Seller,
            coupon.Code,
            coupon.Description,
            coupon.ValidUntil,
            coupon.LastVerifiedAt);
}
