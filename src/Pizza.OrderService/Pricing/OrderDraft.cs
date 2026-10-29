namespace Pizza.OrderService.Pricing;

public sealed record OrderDraft(IReadOnlyList<LineItemDraft>? LineItems, string? PromoCode, TipDraft? Tip);

public sealed record LineItemDraft(string Size, int Quantity, IReadOnlyList<string>? Toppings);

public sealed record TipDraft(TipMode Mode, decimal Value);

public enum TipMode { Amount, Percent }
