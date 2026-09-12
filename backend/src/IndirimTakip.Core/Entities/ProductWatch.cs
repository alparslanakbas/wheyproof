namespace IndirimTakip.Core.Entities;

// Price alert: the shopper wants a one-time notification when a specific
// product's price drops. NOT a full account or permanent watchlist system (full
// membership is a separate, larger item); it reuses the existing Subscriber
// (double opt-in) infrastructure. Once the notification is sent NotifiedAt is
// set and the watch is "consumed", so it doesn't fire again and again.
public class ProductWatch
{
    public int Id { get; set; }
    public int SubscriberId { get; set; }
    public Subscriber? Subscriber { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? NotifiedAt { get; set; }
}
