# Order service

ASP.NET Core minimal API, `net10.0`. Spec: `docs/spec.md` §3, §4, §6, §8.

## Public interfaces

Everything else is `internal`.

- `Pricing/PricingEngine.Price(draft) → PricingResult`: called by the quote endpoint and `OrderBook`.
- `Orders/OrderBook`: `Submit`, `Update`, `Cancel`, `Advance`, `Get`. Called by the order endpoints.
- Endpoints in `Program.cs` only map HTTP to and from these. No logic.

## Money

- `decimal` only. Never `double` or `float`.
- All rounding goes through `Money.RoundCents` (half away from zero), at the four points in spec §3.2. No other rounding calls.
- Money is serialized as JSON numbers.

## Pricing

- Sizes, categories, toppings and promo codes come from the embedded `Pricing/menu.json`. No `switch` or `if` on names.
- Promo kinds are `IPromotion` implementations; codes are data. Conflicts go through exclusivity groups, never by naming another promo.
- If adding a menu item or a promo of an existing kind would touch `PricingEngine`, stop and tell me.
- Nothing under `Pricing/` references ASP.NET.

## OrderBook

- One lock. Each operation (idempotency lookup, status check, change, store) runs entirely inside it. Never check in one step and write in another.
- Allowed transitions live in one table in `Order.cs`.
- Submit and update always reprice. Client prices are never used for pricing; `expectedTotal` is only compared (spec §6.1).
- Idempotency follows spec §6.2: keys scoped by operation, only 2xx responses stored.

## Packages

Framework only.
