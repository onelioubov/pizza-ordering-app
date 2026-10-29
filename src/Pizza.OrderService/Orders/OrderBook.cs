using Pizza.OrderService.Pricing;

namespace Pizza.OrderService.Orders;

/// <summary>
/// The in-memory store of orders and their lifecycle (spec §6). Every operation runs entirely under one
/// lock, so status changes are linearizable. Not durable; one instance only.
/// </summary>
public sealed class OrderBook(PricingEngine engine)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Order> _orders = [];

    /// <summary>Prices the draft and stores it as a new NotStarted order.</summary>
    /// <exception cref="InvalidDraftException">The draft breaks a rule in spec §4, including an unknown promo code.</exception>
    public Order Submit(OrderDraft draft)
    {
        lock (_lock)
        {
            var order = new Order($"ord_{Guid.CreateVersion7():N}", OrderStatus.NotStarted, draft, engine.Price(draft, rejectUnknownPromo: true));
            _orders.Add(order.Id, order);
            return order;
        }
    }

    /// <exception cref="OrderNotFoundException" />
    public Order Get(string id)
    {
        lock (_lock)
            return Find(id);
    }

    /// <summary>Moves the order one step along the kitchen's sequence (simulated restaurant, spec §6).</summary>
    /// <exception cref="OrderNotFoundException" />
    /// <exception cref="OrderStatusConflictException">The order is Delivered or Cancelled.</exception>
    public Order Advance(string id) => Apply(id, OrderAction.Advance);

    /// <summary>Replaces the whole draft and reprices it. Only while the order is NotStarted (spec §6).</summary>
    /// <exception cref="OrderNotFoundException" />
    /// <exception cref="OrderStatusConflictException">The kitchen has started, or the order is Cancelled.</exception>
    /// <exception cref="InvalidDraftException">The draft breaks a rule in spec §4, including an unknown promo code.</exception>
    public Order Update(string id, OrderDraft draft) =>
        Apply(id, OrderAction.Update, order => order with { Draft = draft, Pricing = engine.Price(draft, rejectUnknownPromo: true) });

    /// <summary>Cancels a NotStarted order. Cancelling a cancelled order returns it unchanged.</summary>
    /// <exception cref="OrderNotFoundException" />
    /// <exception cref="OrderStatusConflictException">The kitchen has started, or the order is Delivered.</exception>
    public Order Cancel(string id) => Apply(id, OrderAction.Cancel);

    /// <summary>Checks the transition, then applies <paramref name="change"/> and the new status, all under the lock.</summary>
    private Order Apply(string id, OrderAction action, Func<Order, Order>? change = null)
    {
        lock (_lock)
        {
            var order = Find(id);
            if (!OrderTransitions.TryApply(order.Status, action, out var next))
                throw new OrderStatusConflictException(order);
            var changed = change is null ? order : change(order);
            return _orders[id] = changed with { Status = next };
        }
    }

    private Order Find(string id) =>
        _orders.TryGetValue(id, out var order) ? order : throw new OrderNotFoundException(id);
}
