using System.Text.Json.Serialization;

// Shape of Shopify's public products.json endpoint. Nothing in it is
// store-specific, so it lives at the shared level rather than under one
// store's folder: every Shopify store uses the same types.
namespace IndirimTakip.Infrastructure.Scraping;

internal sealed class ShopifyProductsResponse
{
    [JsonPropertyName("products")]
    public List<ShopifyProduct> Products { get; set; } = [];
}

internal sealed class ShopifyProduct
{
    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("handle")]
    public required string Handle { get; set; }

    // Brand name as the store records it. For a single-brand store this is
    // the brand itself; for a retailer it is the actual manufacturer.
    [JsonPropertyName("vendor")]
    public string? Vendor { get; set; }

    [JsonPropertyName("product_type")]
    public string? ProductType { get; set; }

    [JsonPropertyName("body_html")]
    public string? BodyHtml { get; set; }

    [JsonPropertyName("images")]
    public List<ShopifyImage> Images { get; set; } = [];

    [JsonPropertyName("variants")]
    public List<ShopifyVariant> Variants { get; set; } = [];

    // Option definitions in position order (1..3). Variant option1..option3
    // hold the values; the names tell size apart from flavor.
    [JsonPropertyName("options")]
    public List<ShopifyOption> Options { get; set; } = [];

    // The store's own tags. Few stores tag consistently; Ghost marks its
    // hidden parent records with "base_product" (see ShopifyStoreScraper).
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}

internal sealed class ShopifyImage
{
    [JsonPropertyName("src")]
    public required string Src { get; set; }
}

internal sealed class ShopifyVariant
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("option1")]
    public string? Option1 { get; set; }

    [JsonPropertyName("option2")]
    public string? Option2 { get; set; }

    [JsonPropertyName("option3")]
    public string? Option3 { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("compare_at_price")]
    public decimal? CompareAtPrice { get; set; }

    [JsonPropertyName("available")]
    public bool Available { get; set; }
}

internal sealed class ShopifyOption
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("position")]
    public int Position { get; set; }
}
