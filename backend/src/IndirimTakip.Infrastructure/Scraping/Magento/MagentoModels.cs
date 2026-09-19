using System.Text.Json.Serialization;

namespace IndirimTakip.Infrastructure.Scraping.Magento;

// The subset of Magento's storefront GraphQL schema the scraper reads.

internal sealed class MagentoResponse
{
    [JsonPropertyName("data")] public MagentoData? Data { get; set; }
    [JsonPropertyName("errors")] public List<MagentoError>? Errors { get; set; }
}

internal sealed class MagentoError
{
    [JsonPropertyName("message")] public string? Message { get; set; }
}

internal sealed class MagentoData
{
    [JsonPropertyName("products")] public MagentoProducts? Products { get; set; }
}

internal sealed class MagentoProducts
{
    [JsonPropertyName("items")] public List<MagentoProduct> Items { get; set; } = [];
}

internal sealed class MagentoProduct
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("url_key")] public string UrlKey { get; set; } = "";
    [JsonPropertyName("stock_status")] public string? StockStatus { get; set; }
    [JsonPropertyName("small_image")] public MagentoImage? SmallImage { get; set; }
    [JsonPropertyName("price_range")] public MagentoPriceRange? PriceRange { get; set; }
    [JsonPropertyName("configurable_options")] public List<MagentoOption>? ConfigurableOptions { get; set; }
    [JsonPropertyName("variants")] public List<MagentoVariant>? Variants { get; set; }
}

internal sealed class MagentoImage
{
    [JsonPropertyName("url")] public string? Url { get; set; }
}

internal sealed class MagentoPriceRange
{
    [JsonPropertyName("minimum_price")] public MagentoPrice? MinimumPrice { get; set; }
}

internal sealed class MagentoPrice
{
    [JsonPropertyName("final_price")] public MagentoMoney? FinalPrice { get; set; }
    [JsonPropertyName("regular_price")] public MagentoMoney? RegularPrice { get; set; }
}

internal sealed class MagentoMoney
{
    [JsonPropertyName("value")] public decimal? Value { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
}

internal sealed class MagentoOption
{
    [JsonPropertyName("attribute_code")] public string AttributeCode { get; set; } = "";
    [JsonPropertyName("attribute_id")] public string? AttributeId { get; set; }
}

internal sealed class MagentoVariant
{
    [JsonPropertyName("attributes")] public List<MagentoVariantAttribute> Attributes { get; set; } = [];
    [JsonPropertyName("product")] public MagentoVariantProduct? Product { get; set; }
}

internal sealed class MagentoVariantAttribute
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("value_index")] public int ValueIndex { get; set; }
}

internal sealed class MagentoVariantProduct
{
    [JsonPropertyName("stock_status")] public string? StockStatus { get; set; }
    [JsonPropertyName("price_range")] public MagentoPriceRange? PriceRange { get; set; }
}
