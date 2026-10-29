import { useEffect, useState } from 'react';
import { ApiError, getMenu, type Menu, type Order, type TipMode } from './orderServiceClient';
import { useOrderDraft } from './useOrderDraft';
import { useOrderTracking } from './useOrderTracking';
import { LineItemEditor } from './components/LineItemEditor';
import { QuoteSummary } from './components/QuoteSummary';
import { OrderView } from './components/OrderView';

export default function App() {
  const [menu, setMenu] = useState<Menu | null>(null);
  const [menuError, setMenuError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [order, setOrder] = useState<Order | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    getMenu(controller.signal).then(
      (m) => {
        setMenu(m);
        setMenuError(null);
      },
      (e: unknown) => {
        if (!controller.signal.aborted) setMenuError(e instanceof ApiError ? e.message : "We couldn't load the menu.");
      },
    );
    return () => controller.abort();
  }, [attempt]);

  return (
    <main>
      <h1>Pizza order builder</h1>
      {order ? (
        <OrderTracker key={order.id} order={order} onNewOrder={() => setOrder(null)} />
      ) : menu ? (
        <OrderBuilder menu={menu} onSubmitted={setOrder} />
      ) : menuError ? (
        <p role="alert">
          {menuError}{' '}
          <button type="button" onClick={() => setAttempt((a) => a + 1)}>
            Retry
          </button>
        </p>
      ) : (
        <p>Loading menu…</p>
      )}
    </main>
  );
}

function OrderTracker({ order, onNewOrder }: { order: Order; onNewOrder: () => void }) {
  const tracking = useOrderTracking(order);
  return (
    <OrderView
      order={tracking.order}
      error={tracking.error}
      terminal={tracking.terminal}
      busy={tracking.busy}
      onRefresh={tracking.refresh}
      onAdvance={tracking.advance}
      onNewOrder={onNewOrder}
    />
  );
}

function OrderBuilder({ menu, onSubmitted }: { menu: Menu; onSubmitted: (order: Order) => void }) {
  const draft = useOrderDraft(menu);
  const { form } = draft;

  return (
    <>
      {form.lineItems.map((item, index) => (
        <LineItemEditor
          key={item.key}
          menu={menu}
          index={index}
          item={item}
          onSize={(size) => draft.setSize(item.key, size)}
          onQuantity={(text) => draft.setQuantity(item.key, text)}
          onToggleTopping={(topping) => draft.toggleTopping(item.key, topping)}
          onRemove={() => draft.removeLineItem(item.key)}
        />
      ))}
      <button type="button" onClick={draft.addLineItem}>
        Add pizza
      </button>

      <p>
        <label>
          Promo code <input type="text" value={form.promoCode} onChange={(e) => draft.setPromoCode(e.target.value)} />
        </label>
      </p>
      <p>
        <label>
          Tip{' '}
          <input type="text" inputMode="decimal" size={6} value={form.tipValue} onChange={(e) => draft.setTipValue(e.target.value)} />
        </label>{' '}
        <select value={form.tipMode} onChange={(e) => draft.setTipMode(e.target.value as TipMode)}>
          <option value="percent">%</option>
          <option value="amount">$</option>
        </select>
      </p>

      <QuoteSummary quote={draft.quote} outOfDate={draft.outOfDate} failed={draft.failed} error={draft.error} />

      <button
        type="button"
        disabled={!draft.canSubmit}
        onClick={() => void draft.submit().then((order) => order && onSubmitted(order))}
      >
        {draft.submitting ? 'Submitting…' : 'Submit order'}
      </button>
      {draft.submitError && (
        <ul role="alert" style={{ color: 'crimson' }}>
          {draft.submitError.messages.map((m, i) => (
            <li key={i}>{m}</li>
          ))}
        </ul>
      )}
    </>
  );
}
