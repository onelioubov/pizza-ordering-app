import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, advanceOrder, getOrder, TERMINAL_STATUSES, type Order } from './orderServiceClient';

const POLL_MS = 3000;

/**
 * A submitted order kept current (spec §7): polls its status every 3 s until it reaches a terminal
 * status. `refresh` fetches now; `advance` plays the kitchen. Only the latest request's response is
 * shown, so a slow poll can't put back an older status. A failed fetch keeps the last order and sets
 * `error`; polling carries on.
 */
export function useOrderTracking(initial: Order) {
  const [order, setOrder] = useState(initial);
  const [error, setError] = useState<ApiError | null>(null);
  const [busy, setBusy] = useState(false);
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

  return {
    order,
    error,
    terminal,
    /** True while a refresh or advance is in flight. */
    busy,
    refresh: () => run(() => getOrder(order.id)),
    advance: () => run(() => advanceOrder(order.id)),
  };
}
