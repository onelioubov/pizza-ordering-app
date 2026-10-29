namespace Pizza.OrderService.Pricing;

public sealed record PricingResult(
    IReadOnlyList<PricedLineItem> LineItems,
    decimal Subtotal,
    PromoResult? Promo,
    decimal Discount,
    decimal Tax,
    decimal Tip,
    decimal Total,
    string Description);

/// <summary>One priced pizza line. <see cref="Size"/> and <see cref="Toppings"/> break the description down for display.</summary>
public sealed record PricedLineItem(
    string Description,
    decimal UnitPrice,
    decimal LineTotal,
    int Quantity,
    string Size,
    IReadOnlyList<PricedTopping> Toppings);

/// <summary>A topping and its rounded price on one pizza of this size (spec §3.1, §3.2).</summary>
public sealed record PricedTopping(string Name, decimal Price);

public sealed record PromoResult(string Code, bool Applied, string? Message);
