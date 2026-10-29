namespace Pizza.OrderService.Orders;

/// <summary>No order has this ID.</summary>
public sealed class OrderNotFoundException(string id) : Exception($"Order '{id}' doesn't exist.");

/// <summary>The order's current status doesn't allow the change (spec §6). Carries the order as it is now.</summary>
public sealed class OrderStatusConflictException(Order current)
    : Exception($"Order '{current.Id}' is {current.Status}.")
{
    public Order Current { get; } = current;
}
