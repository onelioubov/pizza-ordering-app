# Pizza ordering: spec

Source of truth for requirements, decisions and the API.

## 1. Overview

- **Order builder** (`src/Pizza.OrderBuilder`): React + TypeScript. Builds the order, shows the server-computed total, submits, tracks, edits and cancels. Does no pricing.
- **Order service** (`src/Pizza.OrderService`): ASP.NET Core minimal API. Owns the menu, pricing, orders, lifecycle and idempotency. Runs as a separate process; the builder reaches it over HTTP.

## 2. Domain

| Concept | Definition |
|---|---|
| Size | `small` $5.00, `medium` $7.00, `large` $12.00 |
| Topping category | `basic` = 8% of the size's base price; `premium` = 11% |
| Topping | Basic: pepperoni, sausage, extra cheese, onions, black olives, green peppers. Premium: bacon, chicken, garlic, artichoke hearts, sun-dried tomatoes, pineapple |
| Promotion | A code, a kind (`PercentOff`, `AmountOffOverThreshold`), parameters and an exclusivity group |
| Line item | Size, quantity, zero or more toppings |
| Order draft | One or more line items, an optional promo code, an optional tip |
| Quote | Priced breakdown of a draft. Stateless; never stored |
| Order | A submitted draft with an ID, a status, and the pricing from its last write |
| Status | `NotStarted`, `Started`, `ReadyForPickup`, `Delivered`, `Cancelled` |

Sizes, categories, toppings and promotions are data in `menu.json`.

## 3. Pricing

### 3.1 Order of operations

1. Topping price = size base price × category rate.
2. Unit price = base price + topping prices. Line total = unit price × quantity.
3. Subtotal = sum of line totals.
4. Discount = from at most one promotion, evaluated against the subtotal.
5. Tax = 6.5% × (subtotal − discount).
6. Tip = dollar amount, or percentage × (subtotal − discount + tax). Not taxed.
7. Total = subtotal − discount + tax + tip.

### 3.2 Rounding

- Round to cents, half away from zero, at exactly four points: each topping price, the discount, the tax and the tip.
- Everything else is exact. The total is the sum of the displayed parts, so the receipt always adds up.

### 3.3 Promotions

| Code | Kind | Effect | Group |
|---|---|---|---|
| `ILOVEPIZZA` | `PercentOff` | 10% off the subtotal | `order-discount` |
| `SAVE5` | `AmountOffOverThreshold` | $5.00 off if the subtotal is over $20.00 | `order-discount` |

- A new promo of an existing kind is a `menu.json` entry. A new kind is one `IPromotion` class.
- Promos in the same group can't be combined. An order holds one code; entering a new one replaces it.
- Codes are case-insensitive and trimmed.
- **Unknown code:** quotes price without a discount and return "Promo code XYZ isn't valid." Submit and update reject it (400).
- **Known but not eligible:** priced with a $0.00 discount and a message ("SAVE5 requires a subtotal over $20.00.") on quotes, submit and update.
- A discount never exceeds the subtotal.

### 3.4 Tip

- `amount`: ≥ 0, at most 2 decimal places.
- `percent`: 0–100, at most 2 decimal places.
- No tip = $0.00.

## 4. Validation

The server validates everything. Errors are `ProblemDetails` with an `errors` map keyed by field, written for a customer to read. The builder also blocks non-numeric text in numeric fields.

| Rule | Message |
|---|---|
| At least one line item | "Add at least one pizza to your order." |
| Size exists | "We don't have a size called 'xl'." |
| Topping exists | "We don't have a topping called 'anchovies'." |
| No duplicate toppings on a pizza | "Pepperoni is already on this pizza." |
| Quantity 1–20 | "Quantity must be between 1 and 20." |
| Tip limits (3.4) | "Tip percentage can't be negative." |
| Known promo code (submit, update) | "Promo code XYZ isn't valid." |

## 5. Order description

Quote and order responses include `description`, one line per line item:

```
2 x Large pizza with bacon, pineapple
1 x Small pizza (no toppings)
```

The builder shows it on "Describe order".

## 6. Lifecycle

| From | To | Trigger |
|---|---|---|
| NotStarted | Started | Restaurant: `advance` |
| Started | ReadyForPickup | Restaurant: `advance` |
| ReadyForPickup | Delivered | Restaurant: `advance` |
| NotStarted | Cancelled | Customer: `cancel` |

- Update replaces the whole draft (line items, promo, tip) and reprices it. Allowed only in `NotStarted`.
- `Delivered` and `Cancelled` are terminal. Cancelling a cancelled order succeeds and returns the order.
- The restaurant is simulated with `POST /api/orders/{id}/advance` and an "Advance (kitchen)" button in the builder. No timer.
- The rules list both Ready for Pickup and Delivered, so the statuses are treated as one linear sequence.

### 6.1 Total confirmation

Submit and update accept an optional `expectedTotal`. The server always reprices. If its total differs, it returns 409 with its pricing and creates or changes nothing. The builder always sends the total it last displayed.

### 6.2 Idempotency

- `Idempotency-Key` is required on submit, update and cancel (400 if missing).
- Keys are scoped by operation. Only successful (2xx) responses are stored, along with a hash of the request.
- Same key and body: replay the stored response. Same key, different body: 422.
- A failed request stores nothing; retrying it runs it again.
- Keys live for the process lifetime. Production would expire them after about 24 hours.

### 6.3 Guarantees

- **One order per submission,** however many retries reach the server.
- **Status changes are linearizable.** Every check-and-change runs under one lock. If cancel and kitchen start race, the first wins and the other gets 409 with the current status.
- **No surprise charges.** The customer is never charged an amount they didn't see (6.1).
- **Prices fixed at last write.** Menu changes don't touch existing orders.
- **Not guaranteed:** durability across restarts, or more than one instance (in-memory store).

## 7. Builder behavior

- Menu comes from `GET /api/menu`. Nothing is hardcoded.
- Every change triggers a quote, debounced 250 ms. Stale responses are ignored.
- If a quote fails, the last total stays visible marked "out of date", the error shows, and Submit is disabled until a fresh quote succeeds.
- Requests time out after 5 s. Mutating requests retry up to 3 times with backoff, reusing the key. After that: "We couldn't confirm your order yet." plus a Retry button that reuses the same key. A new user action gets a new key.
- The order view polls status every 3 s until the order reaches a terminal status, and has a Refresh button.
- "Edit order" loads the order into the builder; "Save changes" sends the update. Conflicts read "The kitchen already started your order." or "Your total changed to $X."
- The running total needs the service. While offline, the UI shows the last total as out of date.

## 8. API

Base path `/api`, JSON. Money is a JSON number with 2 decimal places: the server uses `decimal`, and the client never computes with it.

| Method | Path | Key | Success | Errors |
|---|---|---|---|---|
| GET | `/menu` | – | 200 | – |
| POST | `/quotes` | – | 200 quote | 400 |
| POST | `/orders` | required | 201 order | 400, 409 total changed, 422 |
| GET | `/orders/{id}` | – | 200 order | 404 |
| PUT | `/orders/{id}` | required | 200 order | 400, 404, 409 not NotStarted or total changed, 422 |
| POST | `/orders/{id}/cancel` | required | 200 order | 400, 404, 409, 422 |
| POST | `/orders/{id}/advance` | – | 200 order | 404, 409 terminal |

**Quote**

```http
POST /api/quotes
{
  "lineItems": [{ "size": "large", "quantity": 2, "toppings": ["bacon", "pineapple"] }],
  "promoCode": "ILOVEPIZZA",
  "tip": { "mode": "percent", "value": 15 }
}
```

```json
{
  "lineItems": [{ "description": "2 x Large pizza with bacon, pineapple", "unitPrice": 14.64, "lineTotal": 29.28 }],
  "subtotal": 29.28,
  "promo": { "code": "ILOVEPIZZA", "applied": true, "message": null },
  "discount": 2.93,
  "tax": 1.71,
  "tip": 4.21,
  "total": 32.27,
  "description": "2 x Large pizza with bacon, pineapple"
}
```

**Submit**

```http
POST /api/orders
Idempotency-Key: 7f3c9a1e-...
{ ...quote body..., "expectedTotal": 32.27 }
```

```json
{ "id": "ord_01H...", "status": "NotStarted", "draft": { ...request body without expectedTotal... }, "pricing": { ...quote fields... } }
```

Every order response includes `draft`, the line items, promo code and tip from its last write, so "Edit order" loads what the server holds.

A total mismatch returns 409 with `{ "error": "totalChanged", "pricing": { ... } }`.

## 9. Golden cases

Calculated by hand and checked in code. Tests must match exactly.

| # | Order | Subtotal | Discount | Tax | Tip | Total |
|---|---|---|---|---|---|---|
| G1 | 1 × small | 5.00 | 0.00 | 0.33 | 0.00 | 5.33 |
| G2 | 2 × large + bacon, pineapple; ILOVEPIZZA; 15% tip | 29.28 | 2.93 | 1.71 | 4.21 | 32.27 |
| G3 | 4 × small; SAVE5 (exactly $20.00, not eligible) | 20.00 | 0.00 | 1.30 | 0.00 | 21.30 |
| G4 | 2 × large; SAVE5; $3.00 tip | 24.00 | 5.00 | 1.24 | 3.00 | 23.24 |
| G5 | 3 × medium + pepperoni, chicken; 20% tip | 24.99 | 0.00 | 1.62 | 5.32 | 31.93 |
| G6 | Test-only $8.50 size, 2 × with one basic + one premium | 20.24 | 0.00 | 1.32 | 0.00 | 21.56 |

- G1: 0.325 rounds up. G4: 1.235 rounds up.
- G2: 12.00 + 1.32 + 1.32 = 14.64; × 2 = 29.28; 10% = 2.928 → 2.93; 26.35 × 6.5% = 1.71275 → 1.71; 28.06 × 15% = 4.209 → 4.21.
- G6: toppings 0.68 and 0.935 → 0.94; unit price 10.12; × 2 = 20.24.

## 10. Invariants to test

Listed in priority order; cut from the bottom.

1. G1–G6 match exactly.
2. Total = subtotal − discount + tax + tip for every quote.
3. The same draft gets the same total from quotes and orders.
4. SAVE5 isn't eligible at $20.00 and is eligible above it.
5. Bad input is rejected with messages: empty order, unknown size or topping, duplicate topping, quantity out of range, tip out of range, unknown code on submit.
6. Illegal transitions return 409.
7. An `expectedTotal` mismatch returns 409 and creates nothing.
8. Ten concurrent submits with one key create one order; the same key with a different body returns 422.
9. Cancel racing advance, repeated a few hundred times: exactly one wins.
10. A new promo or size works through data or a new `IPromotion` alone.
11. The client reuses the key on retries and makes a new one per action.

Not planned within at the moment: `useOrderDraft` tests, UI tests.

## 11. Running

- **Dev:** service on http://localhost:5080. The builder's Vite dev server proxies `/api` to it.
- **Containers:** `docker compose up`. Builder on http://localhost:8080 (nginx, proxies `/api`); service on http://localhost:5080.
