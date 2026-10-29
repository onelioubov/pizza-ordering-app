import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, advanceOrder, cancelOrder, getOrder, TERMINAL_STATUSES, type Order } from './orderServiceClient';

const POLL_MS = 3000;

/**
 * A submitted order kept current (spec §7): polls its status every 3 s until it reaches a terminal
 * status. `refresh` fetches now; `advance` plays the kitchen; `cancel` is the customer's. Only the latest request's response is
 * shown, so a slow poll can't put back an older status. A failed fetch keeps the last order and sets
 * `error`; polling carries on. An unconfirmed cancel can be retried with its original key.
 */
export function useOrderTracking(initial: Order) {
  const [order, setOrder] = useState(initial);
  const [error, setError] = useState<ApiError | null>(null);
  const [busy, setBusy] = useState(false);
  // The key of the last cancel that got no answer; Retry resends it (spec §7).
  const [unconfirmedCancelKey, setUnconfirmedCancelKey] = useState<string | null>(null);
  const latest = useRef(0);
  const terminal = TERMINAL_STATUSES.includes(order.status);

  /** Sends a request and applies its outcome only if no newer request has started since. */
  const track = useCallback((call: () => Promise<Order>) => {
    const id = ++latest.current;
    return call().then(
      (next) => {
        if (id !== latest.current) return;
        setOrder(next);
        setError(null);
      },
      (e: unknown) => {
        if (id !== latest.current) return;
        const apiError = e instanceof ApiError ? e : new ApiError('Something went wrong.');
        if (apiError.currentOrder) setOrder(apiError.currentOrder);
        setError(apiError);
      },
    );
  }, []);

  useEffect(() => {
    if (terminal) return;
    // A chain rather than an interval, so polls never overlap.
    let timer: ReturnType<typeof setTimeout>;
    let stopped = false;
    const poll = () => {
      timer = setTimeout(() => {
        void track(() => getOrder(order.id)).finally(() => !stopped && poll());
      }, POLL_MS);
    };
    poll();
    return () => {
      stopped = true;
      clearTimeout(timer);
      latest.current++; // drop any poll still in flight
    };
  }, [order.id, terminal, track]);

  const run = (call: () => Promise<Order>) => {
    setBusy(true);
    void track(call).finally(() => setBusy(false));
  };

  const sendCancel = (key: string) => {
    setUnconfirmedCancelKey(null);
    run(() =>
      cancelOrder(order.id, key).catch((e: unknown) => {
        if (e instanceof ApiError && e.unconfirmed) setUnconfirmedCancelKey(key);
        throw e;
      }),
    );
  };

  return {
    order,
    error,
    terminal,
    /** True while a refresh or advance is in flight. */
    busy,
    refresh: () => run(() => getOrder(order.id)),
    advance: () => run(() => advanceOrder(order.id)),
    // A new key per click (spec §6.2).
    cancel: () => sendCancel(crypto.randomUUID()),
    /** True when the last cancel went unconfirmed and the order still looks cancellable; offer `retryCancel`. */
    canRetryCancel: unconfirmedCancelKey !== null && !busy && order.status === 'NotStarted',
    retryCancel: () => unconfirmedCancelKey && sendCancel(unconfirmedCancelKey),
  };
}
