using System.Security.Cryptography;
using System.Text.Json;
using Pizza.OrderService.Pricing;

namespace Pizza.OrderService.Orders;

/// <summary>
/// The in-memory store of orders and their lifecycle (spec §6). Every operation, including its idempotency
/// lookup, runs entirely under one lock, so status changes are linearizable and one key makes one change.
/// Not durable; one instance only.
/// </summary>
public sealed class OrderBook(PricingEngine engine)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Order> _orders = [];
    // Successful results by operation and key, with a hash of the request that produced them (spec §6.2).
    // Kept for the process lifetime; production would expire them after about 24 hours.
    private readonly Dictionary<(OrderAction Action, string Key), (string RequestHash, Order Result)> _completed = [];

    /// <summary>
    /// Prices the draft and stores it as a new NotStarted order. A repeated key with the same request returns
    /// the order it created.
    /// </summary>
    /// <param name="expectedTotal">The total the customer saw; if given and different, nothing is created.</param>
    /// <exception cref="InvalidDraftException">The draft breaks a rule in spec §4, including an unknown promo code.</exception>
    /// <exception cref="TotalChangedException">The server's total differs from <paramref name="expectedTotal"/>.</exception>
    /// <exception cref="IdempotencyKeyReusedException">The key was used for a different submit.</exception>
    public Order Submit(OrderDraft draft, decimal? expectedTotal, string idempotencyKey) =>
        Once(OrderAction.Submit, idempotencyKey, new { draft, expectedTotal }, () =>
        {
            var pricing = PriceConfirmed(draft, expectedTotal);
            var order = new Order($"ord_{Guid.CreateVersion7():N}", OrderStatus.NotStarted, draft, pricing);
            _orders.Add(order.Id, order);
            return order;
        });

    /// <exception cref="OrderNotFoundException" />
    public Order Get(string id)
    {
        lock (_lock)
            return Find(id);
    }

    /// <summary>Moves the order one step along the kitchen's sequence (simulated restaurant, spec §6).</summary>
    /// <exception cref="OrderNotFoundException" />
    /// <exception cref="OrderStatusConflictException">The order is Delivered or Cancelled.</exception>
    public Order Advance(string id)
    {
        lock (_lock)
            return Apply(id, OrderAction.Advance);
    }

    /// <summary>
    /// Replaces the whole draft and reprices it. Only while the order is NotStarted (spec §6). A repeated key
    /// with the same request returns the order as that update left it.
    /// </summary>
    /// <param name="expectedTotal">The total the customer saw; if given and different, nothing changes.</param>
    /// <exception cref="OrderNotFoundException" />
    /// <exception cref="OrderStatusConflictException">The kitchen has started, or the order is Cancelled.</exception>
    /// <exception cref="InvalidDraftException">The draft breaks a rule in spec §4, including an unknown promo code.</exception>
    /// <exception cref="TotalChangedException">The server's total differs from <paramref name="expectedTotal"/>.</exception>
    /// <exception cref="IdempotencyKeyReusedException">The key was used for a different update.</exception>
    public Order Update(string id, OrderDraft draft, decimal? expectedTotal, string idempotencyKey) =>
        Once(OrderAction.Update, idempotencyKey, new { id, draft, expectedTotal }, () =>
            Apply(id, OrderAction.Update, order => order with { Draft = draft, Pricing = PriceConfirmed(draft, expectedTotal) }));

    /// <summary>
    /// Cancels a NotStarted order. Cancelling a cancelled order returns it unchanged. A repeated key for the
    /// same order returns the order as that cancel left it.
    /// </summary>
    /// <exception cref="OrderNotFoundException" />
    /// <exception cref="OrderStatusConflictException">The kitchen has started, or the order is Delivered.</exception>
    /// <exception cref="IdempotencyKeyReusedException">The key was used to cancel a different order.</exception>
    public Order Cancel(string id, string idempotencyKey) =>
        Once(OrderAction.Cancel, idempotencyKey, new { id }, () => Apply(id, OrderAction.Cancel));

    /// <summary>
    /// Runs <paramref name="operation"/> under the lock unless this key already succeeded: then replays that
    /// result if the request matches, or refuses if it doesn't. Only successes are stored, so a failed request
    /// can be retried with the same key.
    /// </summary>
    private Order Once(OrderAction action, string key, object request, Func<Order> operation)
    {
        // Hashes the bound request, so numbers that differ only in trailing zeros (15 vs 15.0) count as
        // different bodies. The builder resends identical bytes, so its retries always match.
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        lock (_lock)
        {
            if (_completed.TryGetValue((action, key), out var completed))
                return completed.RequestHash == requestHash ? completed.Result : throw new IdempotencyKeyReusedException(key);
            var result = operation();
            _completed[(action, key)] = (requestHash, result);
            return result;
        }
    }

    /// <summary>Always reprices; the customer's total is only compared, never used (spec §6.1).</summary>
    private PricingResult PriceConfirmed(OrderDraft draft, decimal? expectedTotal)
    {
        var pricing = engine.Price(draft, rejectUnknownPromo: true);
        if (expectedTotal is { } expected && expected != pricing.Total)
            throw new TotalChangedException(pricing);
        return pricing;
    }

    /// <summary>Checks the transition, then applies <paramref name="change"/> and the new status. Caller holds the lock.</summary>
    private Order Apply(string id, OrderAction action, Func<Order, Order>? change = null)
    {
        var order = Find(id);
        if (!OrderTransitions.TryApply(order.Status, action, out var next))
            throw new OrderStatusConflictException(order);
        var changed = change is null ? order : change(order);
        return _orders[id] = changed with { Status = next };
    }

    private Order Find(string id) =>
        _orders.TryGetValue(id, out var order) ? order : throw new OrderNotFoundException(id);
}
