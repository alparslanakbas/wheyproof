using IndirimTakip.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndirimTakip.Infrastructure.Subscribers;

public record WatchProductRequest(string Email);

// Records a price alert request; sending the notification when the price drops
// happens in ProductWatchNotifier (part of the scrape cycle).
public class ProductWatchService(AppDbContext db, SubscriberService subscribers)
{
    public async Task<bool> WatchAsync(int productId, WatchProductRequest request, string confirmBaseUrl, CancellationToken cancellationToken = default)
    {
        var productExists = await db.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
            return false;

        var subscriber = await subscribers.GetOrCreateSubscriberAsync(request.Email, cancellationToken);

        var existingWatch = await db.ProductWatches.FirstOrDefaultAsync(
            w => w.SubscriberId == subscriber.Id && w.ProductId == productId,
            cancellationToken);

        if (existingWatch is null)
        {
            db.ProductWatches.Add(new ProductWatch
            {
                SubscriberId = subscriber.Id,
                ProductId = productId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (existingWatch.NotifiedAt is not null)
        {
            // There's a unique index on (SubscriberId, ProductId): ignoring an
            // already-notified record and inserting a second row hit the index
            // and returned 500. To watch again, the same row is reset.
            existingWatch.NotifiedAt = null;
            existingWatch.CreatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        // else: the watch is already active (NotifiedAt == null); nothing to do.

        // Confirming the newsletter also serves as consent for price alerts; a
        // separate confirmation flow would be overkill for this light feature.
        // Already confirmed means no new mail. The watch itself was created above
        // (the real job), so a failed confirmation email (logged inside
        // SendConfirmationEmailAsync) doesn't fail the request, the same pattern
        // as the watchlist.
        if (!subscriber.IsConfirmed)
            _ = await subscribers.SendConfirmationEmailAsync(subscriber, confirmBaseUrl, cancellationToken);

        return true;
    }
}
