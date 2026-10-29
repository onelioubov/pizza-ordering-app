using System.Text.Json;

namespace Pizza.OrderService.Pricing;

internal sealed record Size(string Id, string Name, decimal BasePrice);

internal sealed record Topping(string Id, string Name, decimal CategoryRate);

/// <summary>Sizes, toppings and promotions parsed from menu.json, indexed for pricing.</summary>
internal sealed class Menu
{
    // Promotion kinds are the only names in code; codes, sizes and toppings are data.
    private static readonly Dictionary<string, Func<string, string, JsonElement, IPromotion>> PromotionKinds = new()
    {
        ["PercentOff"] = (code, group, parameters) => new PercentOffPromotion(code, group, parameters),
        ["AmountOffOverThreshold"] = (code, group, parameters) => new AmountOffOverThresholdPromotion(code, group, parameters),
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public IReadOnlyDictionary<string, Size> Sizes { get; }
    public IReadOnlyDictionary<string, Topping> Toppings { get; }
    public IReadOnlyDictionary<string, IPromotion> Promotions { get; }

    private Menu(MenuFile file)
    {
        Sizes = file.Sizes.ToDictionary(s => s.Id, s => new Size(s.Id, s.Name, s.BasePrice));

        var rates = file.ToppingCategories.ToDictionary(c => c.Id, c => c.Rate);
        Toppings = file.Toppings.ToDictionary(t => t.Id, t => new Topping(t.Id, t.Name,
            rates.TryGetValue(t.Category, out var rate)
                ? rate
                : throw new InvalidOperationException($"Topping '{t.Id}' has unknown category '{t.Category}'.")));

        Promotions = file.Promotions.ToDictionary(p => p.Code, p =>
            PromotionKinds.TryGetValue(p.Kind, out var create)
                ? create(p.Code, p.Group, p.Parameters)
                : throw new InvalidOperationException($"Promotion '{p.Code}' has unknown kind '{p.Kind}'."),
            StringComparer.OrdinalIgnoreCase);
    }

    public static Menu FromJson(string json) =>
        new(JsonSerializer.Deserialize<MenuFile>(json, JsonOptions) ?? throw new InvalidOperationException("Menu JSON is empty."));

    public static Menu LoadEmbedded()
    {
        using var stream = typeof(Menu).Assembly.GetManifestResourceStream("Pizza.OrderService.Pricing.menu.json")
            ?? throw new InvalidOperationException("Embedded menu.json is missing.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }

    private sealed record MenuFile(SizeEntry[] Sizes, CategoryEntry[] ToppingCategories, ToppingEntry[] Toppings, PromotionEntry[] Promotions);
    private sealed record SizeEntry(string Id, string Name, decimal BasePrice);
    private sealed record CategoryEntry(string Id, decimal Rate);
    private sealed record ToppingEntry(string Id, string Name, string Category);
    private sealed record PromotionEntry(string Code, string Kind, string Group, JsonElement Parameters);
}
