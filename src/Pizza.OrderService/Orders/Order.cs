using Pizza.OrderService.Pricing;

namespace Pizza.OrderService.Orders;

public enum OrderStatus { NotStarted, Started, ReadyForPickup, Delivered, Cancelled }

/// <summary>A submitted order with the pricing from its last write (spec §6.3). Immutable; changes replace it.</summary>
public sealed record Order(string Id, OrderStatus Status, PricingResult Pricing);

internal enum OrderAction { Advance }

internal static class OrderTransitions
{
    // Every allowed status change (spec §6). Anything missing here is a 409.
    private static readonly Dictionary<(OrderStatus From, OrderAction Action), OrderStatus> Allowed = new()
    {
        [(OrderStatus.NotStarted, OrderAction.Advance)] = OrderStatus.Started,
        [(OrderStatus.Started, OrderAction.Advance)] = OrderStatus.ReadyForPickup,
        [(OrderStatus.ReadyForPickup, OrderAction.Advance)] = OrderStatus.Delivered,
    };

    public static bool TryApply(OrderStatus from, OrderAction action, out OrderStatus to) =>
        Allowed.TryGetValue((from, action), out to);
}
