import { useEffect, useState } from 'react';
import { ApiError, getMenu, type Menu, type TipMode } from './orderServiceClient';
import { useOrderDraft } from './useOrderDraft';
import { LineItemEditor } from './components/LineItemEditor';
import { QuoteSummary } from './components/QuoteSummary';

export default function App() {
  const [menu, setMenu] = useState<Menu | null>(null);
  const [menuError, setMenuError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

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
      {menu ? (
        <OrderBuilder menu={menu} />
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

function OrderBuilder({ menu }: { menu: Menu }) {
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

      {/* Sends the order in slice 4; gated on a fresh quote now (spec §7). */}
      <button type="button" disabled={!draft.quote || draft.outOfDate || draft.failed}>
        Submit order
      </button>
    </>
  );
}
