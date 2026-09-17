using System.Text.Json.Serialization;

namespace IndirimTakip.Infrastructure.Scraping.Woo;

// The WooCommerce Store API's product shape (/wp-json/wc/store/v1/products).
// Only the fields we read are declared; the endpoint returns many more.
internal sealed class WooProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("permalink")]
    public string Permalink { get; set; } = string.Empty;

    /// <summary>"simple", "variable", "bundle", "grouped".</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("prices")]
    public WooPrices? Prices { get; set; }

    [JsonPropertyName("is_in_stock")]
    public bool IsInStock { get; set; }

    [JsonPropertyName("is_purchasable")]
    public bool IsPurchasable { get; set; }

    [JsonPropertyName("images")]
    public List<WooImage> Images { get; set; } = [];

    [JsonPropertyName("categories")]
    public List<WooTerm> Categories { get; set; } = [];

    [JsonPropertyName("attributes")]
    public List<WooAttribute> Attributes { get; set; } = [];

    [JsonPropertyName("variations")]
    public List<WooVariation> Variations { get; set; } = [];
}

/// <summary>
/// AMOUNTS ARE IN MINOR UNITS: "3900" with CurrencyMinorUnit 2 is $39.00.
/// Read as a number it would be a hundred times the real price — the same trap
/// a Turkish WooCommerce source produced before this scraper existed.
/// </summary>
internal sealed class WooPrices
{
    [JsonPropertyName("price")]
    public string? Price { get; set; }

    [JsonPropertyName("regular_price")]
    public string? RegularPrice { get; set; }

    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("currency_minor_unit")]
    public int CurrencyMinorUnit { get; set; } = 2;
}

internal sealed class WooImage
{
    [JsonPropertyName("src")]
    public string Src { get; set; } = string.Empty;
}

internal sealed class WooTerm
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
}

internal sealed class WooAttribute
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("terms")]
    public List<WooTerm> Terms { get; set; } = [];
}

internal sealed class WooVariation
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("attributes")]
    public List<WooVariationAttribute> Attributes { get; set; } = [];
}

internal sealed class WooVariationAttribute
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The term SLUG ("450g-90-servings"), not its display name.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
