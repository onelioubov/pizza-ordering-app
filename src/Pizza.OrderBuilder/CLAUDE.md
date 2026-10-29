# Order builder

Vite + React + TypeScript. Spec: `docs/spec.md` §7, §8. Plain markup is fine.

## Public modules

- `orderServiceClient.ts`: one function per API operation. Owns `fetch`, the 5 s timeout, retries (3, with backoff, same `Idempotency-Key`) and mapping errors to messages. The only file that calls `fetch`.
- `useOrderDraft.ts`: draft state, debounced (250 ms) quotes, ignoring stale responses, the out-of-date and failed flags.
- Components only render.

## Rules

- Never compute with money. Display server amounts through one `Intl.NumberFormat` (en-US, USD) formatter.
- The menu comes from `GET /api/menu`. No hardcoded sizes, toppings or prices.
- Disable Submit while the quote is out of date or failed. Always send `expectedTotal` = the last displayed total.
- A new `Idempotency-Key` per user action; retries reuse it.
- Catch non-numeric text in numeric inputs. The server validates everything else.
- The Vite dev server proxies `/api` to http://localhost:5080.

## Packages

`react`, `react-dom`, `vite`, `typescript`, `@vitejs/plugin-react`, `@types/react`, `@types/react-dom`.
