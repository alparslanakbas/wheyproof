namespace IndirimTakip.Core.Entities;

// Lightweight watchlist that needs no account or login. It hangs off the same
// Subscriber (email + token) infrastructure, but since no email is sent it never
// enters the confirmation flow (IsConfirmed).
public class ProductFavorite
{
    public int Id { get; set; }
    public int SubscriberId { get; set; }
    public Subscriber? Subscriber { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
