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
  onNewOrder: () => void;
}

export function OrderView({ order, error, terminal, busy, onRefresh, onAdvance, onNewOrder }: Props) {
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

      <button type="button" onClick={onNewOrder}>
        New order
      </button>
    </section>
  );
}
