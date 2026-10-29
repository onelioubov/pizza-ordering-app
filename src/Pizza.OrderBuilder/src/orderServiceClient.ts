// The only module that talks to the order service (spec §8). Money values are server
// amounts: display them, never compute with them.

import { formatMoney } from './money';

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

export interface QuoteLineItem {
  description: string;
  unitPrice: number;
  lineTotal: number;
  quantity: number;
  /** Size name, e.g. "Large". */
  size: string;
  /** Each topping with its price on one pizza. */
  toppings: { name: string; price: number }[];
}

export interface Quote {
  lineItems: QuoteLineItem[];
  subtotal: number;
  promo: { code: string; applied: boolean; message: string | null } | null;
  discount: number;
  tax: number;
  tip: number;
  total: number;
  description: string;
}

export type OrderStatus = 'NotStarted' | 'Started' | 'ReadyForPickup' | 'Delivered' | 'Cancelled';

/** The draft as the server stored it. Fields the customer left out come back as null. */
export interface StoredDraft {
  lineItems: { size: string; quantity: number; toppings: string[] | null }[];
  promoCode: string | null;
  tip: { mode: TipMode; value: number } | null;
}

export interface Order {
  id: string;
  status: OrderStatus;
  draft: StoredDraft;
  pricing: Quote;
}

export const TERMINAL_STATUSES: readonly OrderStatus[] = ['Delivered', 'Cancelled'];

/** A failed request, with messages written for the customer. */
export class ApiError extends Error {
  readonly status: number | null;
  /** Server validation messages keyed by field, e.g. `lineItems[0].quantity`. */
  readonly fieldErrors: Record<string, string[]>;
  /** On a status conflict (409), the order as the server has it now. */
  readonly currentOrder: Order | null;
  /** On a total conflict (409), the server's pricing of the draft that was sent. */
  readonly pricing: Quote | null;
  /**
   * True when a submit, update or cancel never got an answer, even after retries: it may or may not have
   * happened. Retry it with the same `Idempotency-Key` to find out without doing it twice.
   */
  readonly unconfirmed: boolean;

  constructor(
    message: string,
    status: number | null = null,
    details: {
      fieldErrors?: Record<string, string[]>;
      currentOrder?: Order;
      pricing?: Quote;
      unconfirmed?: boolean;
    } = {},
  ) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.fieldErrors = details.fieldErrors ?? {};
    this.currentOrder = details.currentOrder ?? null;
    this.pricing = details.pricing ?? null;
    this.unconfirmed = details.unconfirmed ?? false;
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
/** Waits before each retry of a submit, update or cancel (spec §7): three retries after the first try. */
const RETRY_DELAYS_MS = [250, 500, 1000];

export function getMenu(signal?: AbortSignal): Promise<Menu> {
  return request<Menu>('/menu', { method: 'GET', signal });
}

export function getQuote(draft: OrderDraft, signal?: AbortSignal): Promise<Quote> {
  return request<Quote>('/quotes', { method: 'POST', body: JSON.stringify(draft), signal });
}

/**
 * Submits the draft as a new order. `expectedTotal` is the total the customer last saw (spec §6.1);
 * pass a new `idempotencyKey` per user action, and the same one to retry an `unconfirmed` failure.
 */
export function submitOrder(draft: OrderDraft, expectedTotal: number, idempotencyKey: string): Promise<Order> {
  return mutate<Order>('/orders', {
    method: 'POST',
    body: JSON.stringify({ ...draft, expectedTotal }),
    headers: { 'Idempotency-Key': idempotencyKey },
  });
}

/**
 * Replaces the order's whole draft (spec §6). Only allowed before the kitchen starts. `expectedTotal`
 * is the total the customer last saw; pass a new `idempotencyKey` per user action, and the same one to
 * retry an `unconfirmed` failure.
 */
export function updateOrder(id: string, draft: OrderDraft, expectedTotal: number, idempotencyKey: string): Promise<Order> {
  return mutate<Order>(
    `/orders/${encodeURIComponent(id)}`,
    { method: 'PUT', body: JSON.stringify({ ...draft, expectedTotal }), headers: { 'Idempotency-Key': idempotencyKey } },
    customerChangeConflict,
  );
}

/**
 * Cancels the order. Only allowed before the kitchen starts; cancelling twice is fine. Pass a new
 * `idempotencyKey` per user action, and the same one to retry an `unconfirmed` failure.
 */
export function cancelOrder(id: string, idempotencyKey: string): Promise<Order> {
  return mutate<Order>(
    `/orders/${encodeURIComponent(id)}/cancel`,
    { method: 'POST', headers: { 'Idempotency-Key': idempotencyKey } },
    customerChangeConflict,
  );
}

export function getOrder(id: string, signal?: AbortSignal): Promise<Order> {
  return request<Order>(`/orders/${encodeURIComponent(id)}`, { method: 'GET', signal });
}

/** Simulates the kitchen moving the order to its next status (spec §6). */
export function advanceOrder(id: string): Promise<Order> {
  return request<Order>(`/orders/${encodeURIComponent(id)}/advance`, { method: 'POST' });
}

const STATUS_TEXT: Record<OrderStatus, string> = {
  NotStarted: 'not started',
  Started: 'started',
  ReadyForPickup: 'ready for pickup',
  Delivered: 'delivered',
  Cancelled: 'cancelled',
};

/** How a status reads in a sentence, e.g. "ready for pickup". */
export function describeStatus(status: OrderStatus): string {
  return STATUS_TEXT[status];
}

/** Says why a status conflict (409) refused the change, given the order as it is now. */
type ConflictMessage = (current: Order) => string;

const alreadyInStatus: ConflictMessage = (current) => `This order is already ${describeStatus(current.status)}.`;

/** Update and cancel: the customer was too late (spec §7). */
const customerChangeConflict: ConflictMessage = (current) =>
  current.status === 'Cancelled' ? alreadyInStatus(current) : 'The kitchen already started your order.';

/**
 * Sends a submit, update or cancel, retrying up to three times with backoff when it gets no answer
 * (network error, timeout, 5xx). Every attempt carries the same `Idempotency-Key`, so the server applies
 * it at most once (spec §6.2). Answers from the server (4xx) are final and not retried.
 */
async function mutate<T>(
  path: string,
  init: Omit<RequestInit, 'signal' | 'headers'> & { headers: { 'Idempotency-Key': string } },
  conflictMessage?: ConflictMessage,
): Promise<T> {
  for (let retry = 0; ; retry++) {
    try {
      return await request<T>(path, init, conflictMessage);
    } catch (e) {
      if (!(e instanceof ApiError) || !isTransient(e)) throw e;
      if (retry === RETRY_DELAYS_MS.length) {
        throw new ApiError("We couldn't confirm your order yet.", e.status, { unconfirmed: true });
      }
      await new Promise((resolve) => setTimeout(resolve, RETRY_DELAYS_MS[retry]));
    }
  }
}

/** No answer, or the server failed: the request may or may not have been applied. */
function isTransient(e: ApiError): boolean {
  return e.status === null || e.status >= 500;
}

/**
 * Sends one request with the 5 s timeout. Throws ApiError for anything the customer should see;
 * if the caller's own signal aborts, rethrows the AbortError so the caller can ignore it.
 */
async function request<T>(
  path: string,
  init: RequestInit & { signal?: AbortSignal },
  conflictMessage: ConflictMessage = alreadyInStatus,
): Promise<T> {
  const timeout = AbortSignal.timeout(TIMEOUT_MS);
  const signal = init.signal ? AbortSignal.any([init.signal, timeout]) : timeout;
  try {
    const response = await fetch(`/api${path}`, {
      ...init,
      signal,
      headers: {
        Accept: 'application/json',
        ...(init.body ? { 'Content-Type': 'application/json' } : {}),
        ...(init.headers as Record<string, string> | undefined),
      },
    });
    if (!response.ok) throw await errorFrom(response, conflictMessage);
    return (await response.json()) as T;
  } catch (e) {
    if (e instanceof ApiError || init.signal?.aborted) throw e;
    if (timeout.aborted) throw new ApiError('The order service took too long to respond.');
    throw new ApiError("We couldn't reach the order service.");
  }
}

async function errorFrom(response: Response, conflictMessage: ConflictMessage): Promise<ApiError> {
  if (response.status >= 500) return new ApiError("The order service isn't available right now.", response.status);
  if (response.status === 404) return new ApiError("We couldn't find that order.", response.status);

  const problem = (await response.json().catch(() => null)) as
    | { errors?: Record<string, string[]>; error?: string; order?: Order; pricing?: Quote }
    | null;
  if (problem?.error === 'statusConflict' && problem.order) {
    return new ApiError(conflictMessage(problem.order), response.status, { currentOrder: problem.order });
  }
  if (problem?.error === 'totalChanged' && problem.pricing) {
    return new ApiError(`Your total changed to ${formatMoney(problem.pricing.total)}.`, response.status, {
      pricing: problem.pricing,
    });
  }
  const fieldErrors = problem?.errors ?? {};
  const first = Object.values(fieldErrors).flat()[0];
  return new ApiError(first ?? 'Something went wrong with your request.', response.status, { fieldErrors });
}
