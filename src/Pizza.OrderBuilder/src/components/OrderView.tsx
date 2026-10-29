import type { ApiError, Order } from '../orderServiceClient';
import { describeStatus } from '../orderServiceClient';
import { QuoteSummary } from './QuoteSummary';

interface Props {
  order: Order;
  error: ApiError | null;
  terminal: boolean;
  busy: boolean;
  onRefresh: () => void;
  onAdvance: () => void;
  onEdit: () => void;
  onCancel: () => void;
  /** Shown when the last cancel went unconfirmed; resends it with the same key. */
  onRetryCancel: (() => void) | null;
  onNewOrder: () => void;
}

export function OrderView({
  order,
  error,
  terminal,
  busy,
  onRefresh,
  onAdvance,
  onEdit,
  onCancel,
  onRetryCancel,
  onNewOrder,
}: Props) {
  const changeable = order.status === 'NotStarted' && !busy;
  return (
    <section>
      <p>
        Order <code>{order.id}</code> is <strong>{describeStatus(order.status)}</strong>.{' '}
        <button type="button" onClick={onRefresh} disabled={busy}>
          Refresh
        </button>{' '}
        <button type="button" onClick={onAdvance} disabled={busy || terminal}>
          Advance (kitchen)
        </button>
      </p>

      {error && (
        <ul role="alert" style={{ color: 'crimson' }}>
          {error.messages.map((m, i) => (
            <li key={i}>{m}</li>
          ))}
        </ul>
      )}

      <QuoteSummary quote={order.pricing} outOfDate={false} failed={false} error={null} />

      <p>
        <button type="button" onClick={onEdit} disabled={!changeable}>
          Edit order
        </button>{' '}
        <button type="button" onClick={onCancel} disabled={!changeable}>
          Cancel order
        </button>
        {onRetryCancel && (
          <>
            {' '}
            <button type="button" onClick={onRetryCancel}>
              Retry
            </button>
          </>
        )}
      </p>

      <button type="button" onClick={onNewOrder}>
        New order
      </button>
    </section>
  );
}
