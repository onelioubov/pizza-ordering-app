# How to run the code
> Human-written

- Application: `docker compose up`. This will build the containers and you can then reach the front end on `http://localhost:8080`.
- Tests: `dotnet test`. From the root directory is fine.
  
# Pizza Order Service: API endpoints
>AI-generated, human-verified.

Base URL: `http://localhost:5080/api` (with `docker compose up`, the builder at `http://localhost:8080` proxies `/api` too).

All bodies are JSON. Money is a JSON number with 2 decimal places. Errors are `ProblemDetails` with an `errors` map keyed by field.

| Method | Path                   | Idempotency-Key | Success   | Errors             |
| ------ | ---------------------- | --------------- | --------- | ------------------ |
| GET    | `/menu`                | –               | 200 menu  | –                  |
| POST   | `/quotes`              | –               | 200 quote | 400                |
| POST   | `/orders`              | required        | 201 order | 400, 409, 422      |
| GET    | `/orders/{id}`         | –               | 200 order | 404                |
| PUT    | `/orders/{id}`         | required        | 200 order | 400, 404, 409, 422 |
| POST   | `/orders/{id}/cancel`  | required        | 200 order | 400, 404, 409, 422 |
| POST   | `/orders/{id}/advance` | –               | 200 order | 404, 409           |

## GET /menu

Sizes and toppings, as served from `menu.json`.

```bash
curl http://localhost:5080/api/menu
```

```json
{
  "sizes": [{ "id": "small", "name": "Small", "basePrice": 5.00 }, ...],
  "toppings": [{ "id": "bacon", "name": "bacon", "category": "premium" }, ...]
}
```

## POST /quotes

Prices a draft without storing anything. An unknown promo code still returns a quote (no discount, with a message).

```bash
curl -X POST http://localhost:5080/api/quotes \
  -H "Content-Type: application/json" \
  -d '{
    "lineItems": [{ "size": "large", "quantity": 2, "toppings": ["bacon", "pineapple"] }],
    "promoCode": "ILOVEPIZZA",
    "tip": { "mode": "percent", "value": 15 }
  }'
```

```json
{
  "lineItems": [{
    "description": "2 x Large pizza with bacon, pineapple", "unitPrice": 14.64, "lineTotal": 29.28,
    "quantity": 2, "size": "Large",
    "toppings": [{ "name": "bacon", "price": 1.32 }, { "name": "pineapple", "price": 1.32 }]
  }],
  "subtotal": 29.28,
  "promo": { "code": "ILOVEPIZZA", "applied": true, "message": null },
  "discount": 2.93,
  "tax": 1.71,
  "tip": 4.21,
  "total": 32.27,
  "description": "2 x Large pizza with bacon, pineapple"
}
```

Tip is either `{ "mode": "amount", "value": 5 }` or `{ "mode": "percent", "value": 15 }`, and can be omitted.

## POST /orders

Submits an order. Same body as a quote, plus an optional `expectedTotal` (the total the customer saw).

```bash
curl -X POST http://localhost:5080/api/orders \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: 7f3c9a1e-2b4d-4c1a-9e8f-123456789abc" \
  -d '{
    "lineItems": [{ "size": "large", "quantity": 2, "toppings": ["bacon", "pineapple"] }],
    "promoCode": "ILOVEPIZZA",
    "tip": { "mode": "percent", "value": 15 },
    "expectedTotal": 32.27
  }'
```

```json
{ "id": "ord_01a189fc3df67a3a8aa419ef686be683", "status": "NotStarted", "draft": { ... }, "pricing": { ...quote fields... } }
```

- **409** `{ "error": "totalChanged", "pricing": { ... } }` if the server's total differs from `expectedTotal`. Nothing is created.

- **422** if the key was already used with a different body. Same key and same body replays the original response.

## GET /orders/{id}

```bash
curl http://localhost:5080/api/orders/ord_01a189fc3df67a3a8aa419ef686be683
```

Returns the order: `id`, `status`, `draft` (as last written) and `pricing`.

## PUT /orders/{id}

Replaces the whole draft and reprices it. Only allowed while the order is `NotStarted`.

```bash
curl -X PUT http://localhost:5080/api/orders/ord_01a189fc3df67a3a8aa419ef686be683 \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: 0c1d2e3f-4a5b-4c6d-8e7f-abcdefabcdef" \
  -d '{
    "lineItems": [{ "size": "medium", "quantity": 1, "toppings": ["pepperoni"] }],
    "promoCode": null,
    "tip": { "mode": "amount", "value": 2 },
    "expectedTotal": 10.05
  }'
```

- **409** `{ "error": "statusConflict", "order": { ... } }` if the kitchen already started, or `totalChanged` as above.

## POST /orders/{id}/cancel

Cancels a `NotStarted` order. Cancelling an already cancelled order succeeds.

```bash
curl -X POST http://localhost:5080/api/orders/ord_01a189fc3df67a3a8aa419ef686be683/cancel \
  -H "Idempotency-Key: 5e6f7a8b-9c0d-4e1f-a2b3-c4d5e6f7a8b9"
```

- **409** `statusConflict` with the current order if it has already started.

## POST /orders/{id}/advance

Simulates the kitchen: `NotStarted` → `Started` → `ReadyForPickup` → `Delivered`.

```bash
curl -X POST http://localhost:5080/api/orders/ord_01a189fc3df67a3a8aa419ef686be683/advance
```

- **409** `statusConflict` if the order is `Delivered` or `Cancelled`.

## Example validation error (400)

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "lineItems[0].size": ["We don't have a size called 'xl'."] }
}
```

# Assumptions
> Human-written

| Ambiguous Instruction | Why the Ambiguity | Assumption/Decision Made |
| --------------------- | ----------------- | ------------------------ |
| "Exact arithmetic"    | Does this mean we can round percentage-based line items in order to total up exactly for the customer? Or does this mean that we need to perform the pricing calculations on exact amounts, and only round at the end (and somehow compensate for that in the front end so it's still filling the requirement to total up correctly with whole cents)? | Rounding percentage-based line items is okay, as long as what the customer sees is correct math between line items and total. |
| "SAVE5 cannot be combined with ILOVEPIZZA." | The two given codes are explicitly not to be combined, but can future added codes potentially | One coupon per order, to save time. Can implement stackable coupons in the hypothetical future. |
| Promo codes | Unclear if they need to be case sensitive or not. | Case insensitive. A pizza restaurant probably doesn't have that many coupon codes in effect. It's not a password, nor is it tied to a directory structure (e.g. Linux vs. Windows). Slightly better user experience. |
| "what would you build next?" from the write-up portion | Is it in the scope of the assignment, or in a broader scope (if we moved to make this a live application)? | Answered as if the question were still in the scope of the assignment. |

These are the assumptions made based on ambiguities in the stated requirements only. They do not include decisions made from things unstated, like if there should be a cap on the amount of pizzas a customer can get (I made the decision to cap that at 20), or whether extra toppings are allowed (I decided that they're not, for simplicity in the timebox), etc. I hope that's okay. If I misunderstood what needs to be in the assumptions, I'm happy to provide more decisions I made.

# Design Write-up
> Human-written

*"How do you represent money and calculate the order total? Where is pricing logic located, and does it run in the order builder, the order service, or both? Why?"*

The money is represented in USD, as decimal values that get rounded half way up to the nearest cent (for the percentage-based values). The pricing logic is located inside of the Order Service, not the Order Builder. Because the Order Builder is a front-end UI, having the pricing logic in the front end could potentially allow a savvy customer to manipulate the price before sending it to the Order Service; and it's _generally_ good practice not to have actual business logic be used for display purposes, and should, therefore, live a the service layer (even though we're loose with separation of concerns in this application, especially since there's no formal database layer, and that's okay for this exercise). It doesn't make sense to have the pricing logic in both places because there would be multiple sources of truth for the calculations, it would diminish code readability, it would add to code bloat, and it would require extra logic to potentially reconcile the two pricing calculators.

*"How does your design handle adding a new size, topping category, or promo code, including a promo that has its own conditions and conflicts with other promos?"*

The UI allows only one promo code per order to ensure that neither coupon code conflicts with the other. Right now, outside of the tests, the only place that the `ILOVEPIZZA` and `SAVE5` coupon codes exist is in the "database" (`menu.json`), so if another coupon code needs to be added, it would need to only go into that file (along with whether it's percentage-based or if it's fixed amount-based). Of course, if a future coupon code is meant to stack, then this will need to be accounted for in a future design - a deliberate decision to omit that logic was made to save time. And if additional toppings, topping categories, and sizes need to be added, they would also only need to go into `menu.json`. No other code needs to change in order to make it work.

*"How does your order lifecycle work? Walk through a customer retrying a submission after a timeout, and a customer trying to cancel at the moment the kitchen starts the order. What guarantees do you make, and how does your implementation provide them?"*

- A customer picks the size, quantity, and toppings for a pizza (and potentially adds more), the total updates with each change in option selections. Every time they do this, a call gets made to a quoting endpoint in the backend that runs the pricing logic and captures the most recent order details as a "draft". If the order service is offline, after a certain time, the quote will be marked out of date and the running total will be greyed out/disabled, so selecting new options to add will not add those items/options to the cart. Once the order service is back up, doing another selection will add it - and all the other selections made in that time - to the order ledger. (In a future design, we could add periodic polling to check if the service is back up without having to select another option to make the itemized statement "live" again).
- When the customer hits `submit`, a POST request is made to an `/orders` endpoint that passes in the same draft that was used to calculate the quote, and it includes an idempotency key to ensure that if there's an error in the submission, we can retry the same draft safely. If a timeout happens during the order builder phase, the customer will get an error message and a button they can click to retry the submission. Because each version of the draft creates a new idempotency key, if an order times out, the same idempotency key from the original submission is used to resend the order (so the order doesn't get duplicated). 
- On a successful submission, the same `POST` request returns an order ID. The customer, then, gets taken to a page that gets the order details by that ID (GET `/orders/{id}`) and displays them on the page, along with the order ID itself. On the same page, the order status is shown, and the customer can edit their order as long as the status is "not started". Otherwise, that edit option is greyed out. On the same page is a button that allows us to manually advance the order status (for more controlled testing).
- If the customer chooses to edit their order, the original order form page loads and pre-fills the order draft information (also from the GET `/orders/{id}` request) - with the "Submit" button now being a "Save changes" button because there is an order ID. The adding/removing options to the totaled list follows the same logic as the first bullet point, but this time, the "Save changes" button does a `PUT` request to the `/orders/{id}` endpoint (again, with an idempotency key), since we're making an update to an existing order and not creating a new order. This takes them back to the summary page that lists the new details. They can also go back to the order summary if they don't wish to save the changes.
- Back on the summary page, once we hit the "Advance" button, a `POST` request is made to `/orders/{id}/advance` which changes the order state in the Order Book (in-memory DB) and returns the order object with the new state. The "Edit Order" and "Cancel Order" buttons also become greyed out and no more changes to the order can be made. There is also a validator on the back end to ensure only the orders in a "Not Started" state are updated/canceled (so if we chose to skip the UI and make the request ourselves to change an order that has advanced, we will be unsuccessful). Once the order has been advanced to "Delivered", the "Advance" button becomes deactivated as well. If the customer manages to select "Edit Order" before the status updates on the front end (periodic poll), when they make their changes and select the "Save changes" button, they get an error message stating that the kitchen has already started their order. 
- If we choose to hit "Cancel Order" on the summary page from a "Not Started" order, a `POST` (not a `DELETE`) request is made to the `/orders/{id}/cancel` endpoint which changes the status of the order to "Canceled" and doesn't allow any more advancing or editing. If a customer tries to cancel, and the request to cancel the order takes longer than it takes for the kitchen to advance the order, there's a small race condition in the UI display - the customer will see the correct status (of whichever one comes first in the race), but no error message (despite one of the two endpoints on the backend throwing an error - I checked for concurrency issues in the backend and made sure there were tests for it, but I ran out of time to get to the front end tests for the same thing; a lesson learned).
  
*"What did you deliberately cut or fake to stay in the timebox, and what would you build next?"*

- I initially wanted to support stacking future coupons (if a coupon gets added that allows this behavior), but thought it best to keep things simple unless I have time at the end.
- I decided to implement the UI tests after the core feature work was done, rather than develop them with each feature like I did the Order Service (TDD), and that was a poor choice as I ran out of time. This would be the next thing to implement before doing any other new work. These tests would have caught the UI race condition in the display.
- I chose to make the UI simple - since web design is not graded, I didn't prioritize making it pretty.
- I would have liked better application telemetry from the API to make it easier to troubleshoot should I have ran into an issue. I'm a fan of structured logging, like with Serilog, so implementing something like that would be nice.

# AI Usage
> Human-written

For this assignment, I used Claude chat for refining my project spec and for creating other AI-related files for use with a Claude agent that will then implement the design. 

I rejected Claude's initial few iterations of proposed project structures in lieu of a [clean .NET one I like to reference quite often](https://gist.github.com/davidfowl/ed7564297c61fe9ab814). It also originally wanted me to use xUnit for testing, so I asked it to justify why this is a better choice over NUnit - we ended up going with NUnit.

I used `curl` commands to examine the return values and verify that editing/canceling an order after advancing would, indeed, throw an error, and that it isn't just the front end that's preventing that from happening.

# Invariants tested
> From the AI-generated spec file, with my human notes.
### Golden cases

Calculated by hand and checked in code. Tests must match exactly.
> Testing basic calculations to make sure application does what it says.

|#|Order|Subtotal|Discount|Tax|Tip|Total|
|---|---|---|---|---|---|---|
|G1|1 × small|5.00|0.00|0.33|0.00|5.33|
|G2|2 × large + bacon, pineapple; ILOVEPIZZA; 15% tip|29.28|2.93|1.71|4.21|32.27|
|G3|4 × small; SAVE5 (exactly $20.00, not eligible)|20.00|0.00|1.30|0.00|21.30|
|G4|2 × large; SAVE5; $3.00 tip|24.00|5.00|1.24|3.00|23.24|
|G5|3 × medium + pepperoni, chicken; 20% tip|24.99|0.00|1.62|5.32|31.93|
|G6|Test-only $8.50 size, 2 × with one basic + one premium|20.24|0.00|1.32|0.00|21.56|

- G1: 0.325 rounds up. G4: 1.235 rounds up.
- G2: 12.00 + 1.32 + 1.32 = 14.64; × 2 = 29.28; 10% = 2.928 → 2.93; 26.35 × 6.5% = 1.71275 → 1.71; 28.06 × 15% = 4.209 → 4.21.
- G6: toppings 0.68 and 0.935 → 0.94; unit price 10.12; × 2 = 20.24.

### Invariants to test

Listed in priority order; cut from the bottom.

1. G1–G6 match exactly.
   > Making sure the app generally does what it's supposed to.
2. Total = subtotal − discount + tax + tip for every quote.
   > Need to make sure the pricing logic follows this order of operations consistently.
3. The same draft gets the same total from quotes and orders.
   > Need to make sure the order costs exactly the same as what the customer sees from the quote.
4. SAVE5 isn't eligible at $20.00 and is eligible above it.
   > Edge case for exactly $20. Instructions say the code is valid "over $20".
5. Bad input is rejected with messages: empty order, unknown size or topping, duplicate topping, quantity out of range, tip out of range, unknown code on submit.
   > It's important for a customer to know why an order can't go through - with generic error messaging, it makes for poor user experience (and troubleshooting).
6. Illegal transitions return 409.
   > We need to make sure that cancellations and edits to an order can only happen during the "Not Started" state, and that they throw a 409 "conflict" if they are in an illegal state or overlap.
7. An `expectedTotal` mismatch returns 409 and creates nothing.
   > If the expected quoted total doesn't match the computed total on submit, it doesn't get processed, because what the customer sees must match what's charged.
8. Ten concurrent submits with one key create one order; the same key with a different body returns 422.
   > If something goes wrong, we don't want the customer to be charged multiple times, or charged an amount that doesn't match what they've been quoted, so we need to ensure the order submission is idempotent.
9. Cancel racing advance, repeated a few hundred times: exactly one wins.
   > We need to make sure that both calls (to cancel and to advance) aren't mutating the same order, so only one of the operations completes while the other throws an error. This works in the backend, but the UI doesn't handle the display portion well there (as noted in the write-up).
10. A new promo or size works through data or a new `IPromotion` alone.
    > In order to ensure the pricing logic is extensible (doesn't use hard-coded values there), we need to add new test values to the "database" (.json file)
11. The client reuses the key on retries and makes a new one per action.
    > We need to ensure that the client does, indeed, use the idempotency keys as designed. While I didn't get to the UI tests (which this would be part of), I can see where in the code this is happening, and I've been able to verify this behavior via Developer Tools' Network tab in my browser.

> And just reiterating in its own spot that I didn't get to the UI tests. Typically, I wouldn't apply TDD to them, so I saved them for last, but since this UI is fairly simple enough, in hindsight, I may have been able to handle doing that approach so I could've been better at prioritizing fewer more polished features, like what the assignment specifically asked for, rather than relying on some manual testing while I (unsuccessfully) made my way to the automated UI testing portion 😔