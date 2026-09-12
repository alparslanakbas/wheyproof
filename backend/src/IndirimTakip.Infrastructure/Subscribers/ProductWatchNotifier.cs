using IndirimTakip.Core.Entities;
using IndirimTakip.Infrastructure.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace IndirimTakip.Infrastructure.Subscribers;

// Called as part of the scrape cycle (see ScrapeIngestionService). It only
// checks products whose PRICE CHANGED in that scrape and that have an active
// (requested, not yet notified) price alert: a query over a small watched
// subset, not the whole catalog.
public class ProductWatchNotifier(
    AppDbContext db,
    IEmailSender emailSender,
    IConfiguration configuration,
    ProductImageOptions imageOptions)
{
    public async Task CheckAndNotifyAsync(IReadOnlyCollection<int> touchedProductIds, CancellationToken cancellationToken = default)
    {
        if (touchedProductIds.Count == 0)
            return;

        var activeWatches = await db.ProductWatches
            .Where(w => w.NotifiedAt == null && touchedProductIds.Contains(w.ProductId) && w.Subscriber!.IsConfirmed)
            .Include(w => w.Subscriber)
            .Include(w => w.Product)
            .ToListAsync(cancellationToken);

        if (activeWatches.Count == 0)
            return;

        var frontendBaseUrl = configuration["FrontendBaseUrl"] ?? EmailTemplate.ProductionFrontendUrl;

        foreach (var group in activeWatches.GroupBy(w => w.ProductId))
        {
            var lastTwoPrices = await db.PriceHistories
                .Where(ph => ph.ProductId == group.Key)
                .OrderByDescending(ph => ph.ScrapedAt)
                .Take(2)
                .Select(ph => ph.Price)
                .ToListAsync(cancellationToken);

            // Without two price points (a new product) or without a drop, no
            // notification: the watch stays active until the price really drops.
            if (lastTwoPrices.Count < 2 || lastTwoPrices[0] >= lastTwoPrices[1])
                continue;

            var newPrice = lastTwoPrices[0];
            var oldPrice = lastTwoPrices[1];

            foreach (var watch in group)
            {
                var html = BuildNotifyHtml(watch.Product!, oldPrice, newPrice, frontendBaseUrl, imageOptions.TabanAdres);
                try
                {
                    await emailSender.SendAsync(watch.Subscriber!.Email, $"Price drop: {watch.Product!.Name}", html, cancellationToken);
                    watch.NotifiedAt = DateTimeOffset.UtcNow;
                }
                catch (Exception)
                {
                    // This subscriber's send failed: the watch stays active and is
                    // retried on the next scrape. Keep going so other subscribers
                    // and products still get notified.
                }
            }

            // Saved per group rather than once at the end, so a failure mid-loop
            // doesn't lose the NotifiedAt marks of groups already sent.
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static string BuildNotifyHtml(Product product, decimal oldPrice, decimal newPrice, string frontendBaseUrl, string imageBaseUrl)
    {
        var productUrl = $"{frontendBaseUrl.TrimEnd('/')}/product/{product.Id}";
        var signalImageUrl = EmailTemplate.AssetUrl(frontendBaseUrl, "confirmation-price-signal.jpg");
        var shieldIconUrl = EmailTemplate.AssetUrl(frontendBaseUrl, "trust-shield.png");
        // The local copy is preferred: it's small and doesn't depend on the
        // source's hotlink policy (mail clients fetch images through their own
        // proxies, and some sources answer those with 403).
        var imageUrl = ProductImageStore.GenelAdres(product.LocalImagePath, imageBaseUrl) ?? product.ImageUrl;
        var imageHtml = imageUrl is not null
            ? $"""<img src="{EmailTemplate.Encode(imageUrl)}" alt="{EmailTemplate.Encode(product.Name)}" width="112" height="112" style="display:block;width:112px;height:112px;object-fit:contain;background:#ffffff;margin:0 auto;" />"""
            : """<div style="width:112px;height:112px;background:#f7f8fc;margin:0 auto;"></div>""";

        var content = $"""
            <table class="email-shell" role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" bgcolor="#ffffff" style="width:600px;max-width:600px;background:#ffffff;border:1px solid #e4e6ef;border-radius:16px;overflow:hidden;box-shadow:0 16px 44px rgba(20,24,48,.10);">
              {EmailTemplate.BrandHeader(frontendBaseUrl)}
              <tr>
                <td class="email-hero" background="{EmailTemplate.Encode(signalImageUrl)}" bgcolor="#0e1122" style="padding:40px 38px;background-color:#0e1122;background-image:url('{EmailTemplate.Encode(signalImageUrl)}');background-position:center;background-size:cover;background-repeat:no-repeat;font-family:Arial,Helvetica,sans-serif;">
                  <div style="display:inline-block;border:1px solid #796cbf;background:#2b2741;color:#f5f6fb;font-size:11px;font-weight:800;line-height:16px;letter-spacing:.8px;padding:7px 12px;border-radius:999px;">PRICE ALERT</div>
                  <h1 class="email-title" style="max-width:360px;margin:20px 0 10px;color:#ffffff;font-size:34px;font-weight:800;line-height:1.1;letter-spacing:-1px;">A product you're watching dropped in price</h1>
                  <p style="max-width:330px;margin:0;color:#c7cbe0;font-size:14px;line-height:21px;">See the new price and product details below.</p>
                </td>
              </tr>
              <tr>
                <td class="email-pad" align="center" bgcolor="#ffffff" style="padding:30px 38px 28px;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td class="mobile-block" width="126" valign="middle" align="center">{imageHtml}</td>
                      <td class="mobile-block mobile-center" valign="middle" style="padding-left:20px;font-family:Arial,Helvetica,sans-serif;">
                        <div style="color:#171a2e;font-size:16px;font-weight:800;line-height:22px;">{EmailTemplate.Encode(product.Name)}</div>
                        <div style="margin-top:12px;">
                          <span style="color:#70768a;text-decoration:line-through;font-size:13px;line-height:18px;">{EmailTemplate.Price(oldPrice)}</span>
                          <span style="display:inline-block;margin-left:8px;color:#168453;font-size:20px;font-weight:800;line-height:26px;">{EmailTemplate.Price(newPrice)}</span>
                        </div>
                        <div style="margin-top:18px;">{EmailTemplate.PrimaryButton(productUrl, "View product")}</div>
                      </td>
                    </tr>
                  </table>
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin-top:26px;border-top:1px solid #e4e6ef;">
                    <tr>
                      <td width="38" valign="middle" style="padding-top:18px;"><img src="{EmailTemplate.Encode(shieldIconUrl)}" width="30" height="30" alt="" style="display:block;width:30px;height:30px;"></td>
                      <td valign="middle" style="padding:18px 0 0 8px;font-family:Arial,Helvetica,sans-serif;color:#60667a;font-size:11px;line-height:16px;text-align:left;">This is a one-time alert. To hear about the next drop, set a price alert again on the product page.</td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
            """;

        return EmailTemplate.Document(
            $"The price you're watching for {product.Name} dropped.",
            content);
    }
}
