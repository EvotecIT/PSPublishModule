using System.Globalization;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static Dictionary<string, object?>? BuildOfferModel(Dictionary<string, object?> meta, string prefix)
    {
        var price = ReadMetaDecimal(meta, prefix + ".price");
        var currency = ReadMetaString(meta, prefix + ".price_currency", prefix + ".priceCurrency");
        if (!price.HasValue && string.IsNullOrWhiteSpace(currency))
            return null;
        var offer = new Dictionary<string, object?> { ["@type"] = "Offer" };
        if (price.HasValue)
            offer["price"] = price.Value.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(currency))
            offer["priceCurrency"] = currency;
        foreach (var (key, property) in new[] { ("availability", "availability"), ("condition", "itemCondition"),
                     ("offer_id", "@id"), ("offer_name", "name"), ("offer_url", "url"), ("offer_description", "description") })
        {
            var value = key == "condition"
                ? ReadMetaString(meta, prefix + ".condition", prefix + ".itemCondition")
                : ReadMetaString(meta, prefix + "." + key);
            if (!string.IsNullOrWhiteSpace(value))
                offer[property] = value;
        }
        var quantity = BuildStructuredQuantity(meta, prefix + ".eligible_quantity");
        if (quantity is not null)
            offer["eligibleQuantity"] = quantity;
        var reference = BuildStructuredQuantity(meta, prefix + ".reference_quantity");
        if (reference is not null && price.HasValue)
        {
            var unitPrice = new Dictionary<string, object?>
            {
                ["@type"] = "UnitPriceSpecification",
                ["price"] = price.Value.ToString(CultureInfo.InvariantCulture),
                ["referenceQuantity"] = reference
            };
            if (!string.IsNullOrWhiteSpace(currency))
                unitPrice["priceCurrency"] = currency;
            offer["priceSpecification"] = unitPrice;
        }
        return offer;
    }

    private static Dictionary<string, object?>? BuildStructuredQuantity(Dictionary<string, object?> meta, string prefix)
    {
        var value = ReadMetaDecimal(meta, prefix + ".value");
        var minimum = ReadMetaDecimal(meta, prefix + ".min_value");
        var maximum = ReadMetaDecimal(meta, prefix + ".max_value");
        if (!value.HasValue && !minimum.HasValue && !maximum.HasValue)
            return null;
        var quantity = new Dictionary<string, object?> { ["@type"] = "QuantitativeValue" };
        if (value.HasValue) quantity["value"] = value.Value;
        if (minimum.HasValue) quantity["minValue"] = minimum.Value;
        if (maximum.HasValue) quantity["maxValue"] = maximum.Value;
        var code = ReadMetaString(meta, prefix + ".unit_code");
        var text = ReadMetaString(meta, prefix + ".unit_text");
        if (!string.IsNullOrWhiteSpace(code)) quantity["unitCode"] = code;
        if (!string.IsNullOrWhiteSpace(text)) quantity["unitText"] = text;
        return quantity;
    }
}
