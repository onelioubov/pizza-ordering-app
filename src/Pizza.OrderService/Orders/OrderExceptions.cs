using Pizza.OrderService.Pricing;

namespace Pizza.OrderService.Orders;

/// <summary>No order has this ID.</summary>
public sealed class OrderNotFoundException(string id) : Exception($"Order '{id}' doesn't exist.");

/// <summary>The order's current status doesn't allow the change (spec §6). Carries the order as it is now.</summary>
public sealed class OrderStatusConflictException(Order current)
    : Exception($"Order '{current.Id}' is {current.Status}.")
{
    public Order Current { get; } = current;
}

/// <summary>The server's total differs from the one the customer saw (spec §6.1). Carries the server's pricing.</summary>
public sealed class TotalChangedException(PricingResult pricing)
    : Exception($"The total is now {pricing.Total}.")
{
    public PricingResult Pricing { get; } = pricing;
}

/// <summary>The idempotency key was already used for a different request of the same operation (spec §6.2).</summary>
public sealed class IdempotencyKeyReusedException(string key)
    : Exception($"Idempotency key '{key}' was already used for a different request.");
