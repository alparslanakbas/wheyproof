namespace IndirimTakip.Core.Entities;

// Promo codes the brand or seller doesn't apply automatically and the shopper
// has to enter by hand. Not scraped: codes change often, and showing a wrong or
// expired code really misleads the shopper at checkout. So they are entered and
// verified by hand.
public class Coupon
{
    public int Id { get; set; }
    // A coupon belongs either to a brand or to a seller; never both. The rule is
    // also enforced by a DB check constraint in AppDbContext.
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public string? Seller { get; set; }

    /// <summary>
    /// The code to enter at checkout. NULL = the promotion has NO code and applies
    /// by itself once its condition is met (e.g. an automatic first-order discount
    /// for new members). Showing an empty code would suggest "I need to find a
    /// code"; the UI shows a descriptive label instead.
    /// </summary>
    public string? Code { get; set; }
    public required string Description { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public DateTimeOffset LastVerifiedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
