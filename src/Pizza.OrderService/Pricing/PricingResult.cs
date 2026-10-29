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

public sealed record PricedLineItem(string Description, decimal UnitPrice, decimal LineTotal);

public sealed record PromoResult(string Code, bool Applied, string? Message);
