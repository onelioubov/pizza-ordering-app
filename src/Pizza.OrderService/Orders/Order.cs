using Pizza.OrderService.Pricing;

namespace Pizza.OrderService.Orders;

public enum OrderStatus { NotStarted, Started, ReadyForPickup, Delivered, Cancelled }

/// <summary>
/// A submitted order: the draft and pricing from its last write (spec §6.3). The draft lets the builder
/// load the order for editing. Immutable; changes replace it.
/// </summary>
public sealed record Order(string Id, OrderStatus Status, OrderDraft Draft, PricingResult Pricing);

internal enum OrderAction { Advance, Update, Cancel }

internal static class OrderTransitions
{
    // Every allowed status change (spec §6). Anything missing here is a 409.
    private static readonly Dictionary<(OrderStatus From, OrderAction Action), OrderStatus> Allowed = new()
    {
        [(OrderStatus.NotStarted, OrderAction.Advance)] = OrderStatus.Started,
        [(OrderStatus.Started, OrderAction.Advance)] = OrderStatus.ReadyForPickup,
        [(OrderStatus.ReadyForPickup, OrderAction.Advance)] = OrderStatus.Delivered,
        [(OrderStatus.NotStarted, OrderAction.Update)] = OrderStatus.NotStarted,
        [(OrderStatus.NotStarted, OrderAction.Cancel)] = OrderStatus.Cancelled,
        // Cancelling a cancelled order succeeds and returns it unchanged.
        [(OrderStatus.Cancelled, OrderAction.Cancel)] = OrderStatus.Cancelled,
    };

    public static bool TryApply(OrderStatus from, OrderAction action, out OrderStatus to) =>
        Allowed.TryGetValue((from, action), out to);
}
