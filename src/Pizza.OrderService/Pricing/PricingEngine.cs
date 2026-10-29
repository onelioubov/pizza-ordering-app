namespace Pizza.OrderService.Pricing;

/// <summary>Prices an order draft (spec §3) after validating it (spec §4). Stateless and thread-safe.</summary>
public sealed class PricingEngine
{
    private const decimal TaxRate = 0.065m;
    private const int MinQuantity = 1;
    private const int MaxQuantity = 20;

    private readonly Menu _menu;

    public PricingEngine() : this(Menu.LoadEmbedded()) { }

    /// <summary>The menu this engine prices against, for `GET /api/menu`.</summary>
    internal Menu Menu => _menu;

    private PricingEngine(Menu menu) => _menu = menu;

    /// <summary>An engine over a menu other than the embedded one, in the same JSON shape as menu.json.</summary>
    public static PricingEngine FromMenuJson(string menuJson) => new(Menu.FromJson(menuJson));

    /// <exception cref="InvalidDraftException">The draft breaks a rule in spec §4.</exception>
    public PricingResult Price(OrderDraft draft)
    {
        var lines = ValidateLines(draft, out var errors);
        ValidateTip(draft.Tip, errors);
        if (errors.Count > 0)
            throw new InvalidDraftException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));

        var pricedLines = lines.Select(PriceLine).ToList();
        var subtotal = pricedLines.Sum(l => l.LineTotal);

        var (promo, discount) = ApplyPromotion(draft.PromoCode, subtotal);
        var tax = Money.RoundCents(TaxRate * (subtotal - discount));
        var tip = draft.Tip is null ? 0m : Money.RoundCents(draft.Tip.Mode switch
        {
            TipMode.Percent => draft.Tip.Value / 100m * (subtotal - discount + tax),
            _ => draft.Tip.Value,
        });

        return new PricingResult(
            pricedLines,
            subtotal,
            promo,
            discount,
            tax,
            tip,
            subtotal - discount + tax + tip,
            string.Join("\n", pricedLines.Select(l => l.Description)));
    }

    private static PricedLineItem PriceLine((Size Size, int Quantity, IReadOnlyList<Topping> Toppings) line)
    {
        var unitPrice = line.Size.BasePrice + line.Toppings.Sum(t => Money.RoundCents(line.Size.BasePrice * t.CategoryRate));
        var toppingText = line.Toppings.Count == 0
            ? " (no toppings)"
            : " with " + string.Join(", ", line.Toppings.Select(t => t.Name));
        return new PricedLineItem($"{line.Quantity} x {line.Size.Name} pizza{toppingText}", unitPrice, unitPrice * line.Quantity);
    }

    private (PromoResult? Promo, decimal Discount) ApplyPromotion(string? promoCode, decimal subtotal)
    {
        var code = promoCode?.Trim();
        if (string.IsNullOrEmpty(code))
            return (null, 0m);

        if (!_menu.Promotions.TryGetValue(code, out var promotion))
            return (new PromoResult(code, false, $"Promo code {code} isn't valid."), 0m);

        var outcome = promotion.Evaluate(subtotal);
        if (outcome.IneligibleMessage is not null)
            return (new PromoResult(promotion.Code, false, outcome.IneligibleMessage), 0m);

        return (new PromoResult(promotion.Code, true, null), Math.Min(Money.RoundCents(outcome.Discount), subtotal));
    }

    private List<(Size Size, int Quantity, IReadOnlyList<Topping> Toppings)> ValidateLines(
        OrderDraft draft, out Dictionary<string, List<string>> errors)
    {
        errors = [];
        var lines = new List<(Size, int, IReadOnlyList<Topping>)>();

        if (draft.LineItems is null || draft.LineItems.Count == 0)
        {
            AddError(errors, "lineItems", "Add at least one pizza to your order.");
            return lines;
        }

        for (var i = 0; i < draft.LineItems.Count; i++)
        {
            var item = draft.LineItems[i];
            var prefix = $"lineItems[{i}]";

            if (item is null)
            {
                AddError(errors, prefix, "This pizza is missing its size and quantity.");
                continue;
            }

            if (!_menu.Sizes.TryGetValue(item.Size ?? "", out var size))
                AddError(errors, $"{prefix}.size", $"We don't have a size called '{item.Size}'.");

            if (item.Quantity is < MinQuantity or > MaxQuantity)
                AddError(errors, $"{prefix}.quantity", $"Quantity must be between {MinQuantity} and {MaxQuantity}.");

            var toppings = new List<Topping>();
            foreach (var id in item.Toppings ?? [])
            {
                if (!_menu.Toppings.TryGetValue(id ?? "", out var topping))
                    AddError(errors, $"{prefix}.toppings", $"We don't have a topping called '{id}'.");
                else if (toppings.Contains(topping))
                    AddError(errors, $"{prefix}.toppings", $"{char.ToUpperInvariant(topping.Name[0])}{topping.Name[1..]} is already on this pizza.");
                else
                    toppings.Add(topping);
            }

            if (size is not null)
                lines.Add((size, item.Quantity, toppings));
        }

        return lines;
    }

    private static void ValidateTip(TipDraft? tip, Dictionary<string, List<string>> errors)
    {
        if (tip is null)
            return;

        var message = tip.Mode switch
        {
            TipMode.Percent when tip.Value < 0m => "Tip percentage can't be negative.",
            TipMode.Percent when tip.Value > 100m => "Tip percentage can't be more than 100.",
            TipMode.Amount when tip.Value < 0m => "Tip amount can't be negative.",
            TipMode.Percent or TipMode.Amount when tip.Value % 0.01m != 0m => "Tip can have at most 2 decimal places.",
            TipMode.Percent or TipMode.Amount => null,
            _ => "Tip must be an amount or a percentage.",
        };
        if (message is not null)
            AddError(errors, "tip", message);
    }

    private static void AddError(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var messages))
            errors[field] = messages = [];
        messages.Add(message);
    }
}
