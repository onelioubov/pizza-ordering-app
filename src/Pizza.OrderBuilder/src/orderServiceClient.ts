// The only module that talks to the order service (spec §8). Money values are server
// amounts: display them, never compute with them.

export interface MenuSize {
  id: string;
  name: string;
  basePrice: number;
}

export interface MenuTopping {
  id: string;
  name: string;
  category: string;
}

export interface Menu {
  sizes: MenuSize[];
  toppings: MenuTopping[];
}

export type TipMode = 'amount' | 'percent';

export interface LineItemDraft {
  size: string;
  quantity: number;
  toppings: string[];
}

export interface OrderDraft {
  lineItems: LineItemDraft[];
  promoCode?: string;
  tip?: { mode: TipMode; value: number };
}

export interface Quote {
  lineItems: { description: string; unitPrice: number; lineTotal: number }[];
  subtotal: number;
  promo: { code: string; applied: boolean; message: string | null } | null;
  discount: number;
  tax: number;
  tip: number;
  total: number;
  description: string;
}

/** A failed request, with messages written for the customer. */
export class ApiError extends Error {
  readonly status: number | null;
  /** Server validation messages keyed by field, e.g. `lineItems[0].quantity`. */
  readonly fieldErrors: Record<string, string[]>;

  constructor(message: string, status: number | null = null, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.fieldErrors = fieldErrors;
  }

  /** Every message to show, field messages first, labelled by pizza where they belong to one. */
  get messages(): string[] {
    const fields = Object.entries(this.fieldErrors).flatMap(([field, messages]) => {
      const pizza = /^lineItems\[(\d+)\]/.exec(field);
      return messages.map((m) => (pizza ? `Pizza ${Number(pizza[1]) + 1}: ${m}` : m));
    });
    return fields.length > 0 ? fields : [this.message];
  }
}

const TIMEOUT_MS = 5000;

export function getMenu(signal?: AbortSignal): Promise<Menu> {
  return request<Menu>('/menu', { method: 'GET', signal });
}

export function getQuote(draft: OrderDraft, signal?: AbortSignal): Promise<Quote> {
  return request<Quote>('/quotes', { method: 'POST', body: JSON.stringify(draft), signal });
}

/**
 * Sends one request with the 5 s timeout. Throws ApiError for anything the customer should see;
 * if the caller's own signal aborts, rethrows the AbortError so the caller can ignore it.
 */
async function request<T>(path: string, init: RequestInit & { signal?: AbortSignal }): Promise<T> {
  const timeout = AbortSignal.timeout(TIMEOUT_MS);
  const signal = init.signal ? AbortSignal.any([init.signal, timeout]) : timeout;
  try {
    const response = await fetch(`/api${path}`, {
      ...init,
      signal,
      headers: { Accept: 'application/json', ...(init.body ? { 'Content-Type': 'application/json' } : {}) },
    });
    if (!response.ok) throw await errorFrom(response);
    return (await response.json()) as T;
  } catch (e) {
    if (e instanceof ApiError || init.signal?.aborted) throw e;
    if (timeout.aborted) throw new ApiError('The order service took too long to respond.');
    throw new ApiError("We couldn't reach the order service.");
  }
}

async function errorFrom(response: Response): Promise<ApiError> {
  if (response.status >= 500) return new ApiError("The order service isn't available right now.", response.status);

  const problem = (await response.json().catch(() => null)) as { errors?: Record<string, string[]> } | null;
  const fieldErrors = problem?.errors ?? {};
  const first = Object.values(fieldErrors).flat()[0];
  return new ApiError(first ?? 'Something went wrong with your request.', response.status, fieldErrors);
}
