namespace Pizza.OrderService.Pricing;

/// <summary>The draft broke one or more rules in spec §4. Errors are keyed by field and written for a customer.</summary>
public sealed class InvalidDraftException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("The order draft is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
