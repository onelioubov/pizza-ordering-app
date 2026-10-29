using System.Globalization;
using System.Text.Json;

namespace Pizza.OrderService.Pricing;

/// <summary>One promotion kind. Codes and parameters are data in menu.json; a new kind is a new implementation.</summary>
internal interface IPromotion
{
    string Code { get; }
    string Group { get; }

    /// <summary>Unrounded discount when eligible, otherwise a customer-facing reason.</summary>
    PromotionOutcome Evaluate(decimal subtotal);
}

internal sealed record PromotionOutcome(decimal Discount, string? IneligibleMessage)
{
    public static PromotionOutcome Eligible(decimal discount) => new(discount, null);
    public static PromotionOutcome Ineligible(string message) => new(0m, message);
}

internal sealed class PercentOffPromotion(string code, string group, JsonElement parameters) : IPromotion
{
    private readonly decimal _percent = parameters.GetProperty("percent").GetDecimal();

    public string Code => code;
    public string Group => group;

    public PromotionOutcome Evaluate(decimal subtotal) => PromotionOutcome.Eligible(subtotal * _percent / 100m);
}

internal sealed class AmountOffOverThresholdPromotion(string code, string group, JsonElement parameters) : IPromotion
{
    private readonly decimal _amount = parameters.GetProperty("amount").GetDecimal();
    private readonly decimal _threshold = parameters.GetProperty("threshold").GetDecimal();

    public string Code => code;
    public string Group => group;

    public PromotionOutcome Evaluate(decimal subtotal) => subtotal > _threshold
        ? PromotionOutcome.Eligible(_amount)
        : PromotionOutcome.Ineligible($"{code} requires a subtotal over ${_threshold.ToString("0.00", CultureInfo.InvariantCulture)}.");
}
